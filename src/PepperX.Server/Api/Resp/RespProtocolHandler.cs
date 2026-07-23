namespace PepperX.Server.Api.Resp
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Net.Sockets;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using PepperX.Core;
    using PepperX.Core.Database;
    using PepperX.Core.Enumeration;
    using PepperX.Core.Exceptions;
    using PepperX.Core.Models;
    using PepperX.Core.Requests;
    using PepperX.Core.Services;
    using PepperX.Core.Settings;
    using RedisResp;
    using SyslogLogging;

    /// <summary>
    /// Hosts the Redis RESP protocol surface over the shared PepperX service layer. Each RESP database index
    /// maps to a container; values are stored as binary payloads. Commands on a single connection are
    /// processed in order; INCR-family commands are serialized per key on this node.
    /// </summary>
    public sealed class RespProtocolHandler
    {
        #region Private-Members

        private readonly ContainerService _Containers;
        private readonly ObjectWriteService _Writes;
        private readonly ObjectReadService _Reads;
        private readonly ObjectDeleteService _Deletes;
        private readonly IMetadataDatabaseDriver _Db;
        private readonly RespSettings _Settings;
        private readonly LoggingModule? _Logging;

        private readonly ConcurrentDictionary<Guid, RespConnectionState> _States = new ConcurrentDictionary<Guid, RespConnectionState>();
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _KeyLocks = new ConcurrentDictionary<string, SemaphoreSlim>();

        private RespListener? _Listener;
        private RespInterface? _Interface;
        private CancellationTokenSource? _Cts;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the RESP protocol handler.
        /// </summary>
        /// <param name="containers">Container service.</param>
        /// <param name="writes">Write service.</param>
        /// <param name="reads">Read service.</param>
        /// <param name="deletes">Delete service.</param>
        /// <param name="db">Metadata database driver.</param>
        /// <param name="settings">RESP settings.</param>
        /// <param name="logging">Optional logging module.</param>
        /// <exception cref="ArgumentNullException">A required argument is null.</exception>
        public RespProtocolHandler(ContainerService containers, ObjectWriteService writes, ObjectReadService reads, ObjectDeleteService deletes, IMetadataDatabaseDriver db, RespSettings settings, LoggingModule? logging)
        {
            _Containers = containers ?? throw new ArgumentNullException(nameof(containers));
            _Writes = writes ?? throw new ArgumentNullException(nameof(writes));
            _Reads = reads ?? throw new ArgumentNullException(nameof(reads));
            _Deletes = deletes ?? throw new ArgumentNullException(nameof(deletes));
            _Db = db ?? throw new ArgumentNullException(nameof(db));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Logging = logging;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Start the RESP listener.
        /// </summary>
        public void Start()
        {
            _Cts = new CancellationTokenSource();
            _Listener = new RespListener(_Settings.Port);
            _Interface = new RespInterface(_Listener);

            _Interface.ClientConnectedAction = args => _States[args.GUID] = new RespConnectionState();
            _Interface.ClientDisconnectedAction = args => _States.TryRemove(args.GUID, out _);
            _Interface.ArrayHandler = e =>
            {
                DispatchSync(e);
                return null!;
            };

            _ = _Listener.StartAsync(_Cts.Token);
        }

        /// <summary>
        /// Stop the RESP listener.
        /// </summary>
        public void Stop()
        {
            try { _Cts?.Cancel(); } catch (Exception) { }
            try { _Listener?.Stop(); } catch (Exception) { }
            _Interface?.Dispose();
            _Listener?.Dispose();
            _Cts?.Dispose();
        }

        #endregion

        #region Private-Methods-Dispatch

        private void DispatchSync(RespDataReceivedEventArgs e)
        {
            RespConnectionState state = _States.GetOrAdd(e.ClientGUID, _ => new RespConnectionState());
            List<string> args = ExtractArgs(e.Value);
            if (args.Count == 0) return;

            byte[] response;
            try
            {
                response = DispatchAsync(args, state).GetAwaiter().GetResult();
            }
            catch (PepperXException ex)
            {
                response = RespWire.Error("ERR " + ex.Message);
            }
            catch (Exception ex)
            {
                response = RespWire.Error("ERR " + ex.Message);
            }

            SendToClient(e.ClientGUID, response);
        }

        private async Task<byte[]> DispatchAsync(List<string> args, RespConnectionState state)
        {
            string cmd = args[0].ToUpperInvariant();
            CancellationToken ct = _Cts?.Token ?? CancellationToken.None;

            switch (cmd)
            {
                case "PING": return args.Count > 1 ? RespWire.BulkString(RespWire.ToBytes(args[1])) : RespWire.SimpleString("PONG");
                case "ECHO": return args.Count > 1 ? RespWire.BulkString(RespWire.ToBytes(args[1])) : RespWire.Error("ERR wrong number of arguments for 'echo'");
                case "HELLO": return Hello(args, state);
                case "AUTH": return RespWire.Error("ERR Client sent AUTH, but no password is set");
                case "SELECT": return Select(args, state);
                case "QUIT": return RespWire.SimpleString("OK");
                case "CLIENT": return Client(args, state);
                case "COMMAND": return Command(args);
                case "CONFIG": return Config(args);
                case "INFO": return Info(state);
                case "DBSIZE": return RespWire.Integer(await CountAsync(state, ct).ConfigureAwait(false));
                case "FLUSHDB": return await FlushDbAsync(state, ct).ConfigureAwait(false);
                case "GET": return await GetAsync(state, args, ct).ConfigureAwait(false);
                case "SET": return await SetAsync(state, args, ct).ConfigureAwait(false);
                case "SETNX": return await SetNxAsync(state, args, ct).ConfigureAwait(false);
                case "GETSET": return await GetSetAsync(state, args, ct).ConfigureAwait(false);
                case "GETDEL": return await GetDelAsync(state, args, ct).ConfigureAwait(false);
                case "MGET": return await MGetAsync(state, args, ct).ConfigureAwait(false);
                case "MSET": return await MSetAsync(state, args, ct).ConfigureAwait(false);
                case "DEL":
                case "UNLINK": return await DelAsync(state, args, ct).ConfigureAwait(false);
                case "EXISTS": return await ExistsAsync(state, args, ct).ConfigureAwait(false);
                case "STRLEN": return await StrLenAsync(state, args, ct).ConfigureAwait(false);
                case "TYPE": return await TypeAsync(state, args, ct).ConfigureAwait(false);
                case "TTL":
                case "PTTL": return await TtlAsync(state, args, ct).ConfigureAwait(false);
                case "EXPIRE":
                case "PEXPIRE":
                case "EXPIREAT":
                case "PEXPIREAT": return RespWire.Error("ERR expiration is not supported by PepperX");
                case "KEYS": return await KeysAsync(state, args, ct).ConfigureAwait(false);
                case "SCAN": return await ScanAsync(state, args, ct).ConfigureAwait(false);
                case "INCR": return await IncrByAsync(state, args.Count > 1 ? args[1] : "", 1, ct).ConfigureAwait(false);
                case "INCRBY": return await IncrByAsync(state, args.Count > 1 ? args[1] : "", ParseLong(args, 2, 0), ct).ConfigureAwait(false);
                case "DECR": return await IncrByAsync(state, args.Count > 1 ? args[1] : "", -1, ct).ConfigureAwait(false);
                case "DECRBY": return await IncrByAsync(state, args.Count > 1 ? args[1] : "", -ParseLong(args, 2, 0), ct).ConfigureAwait(false);
                case "INCRBYFLOAT": return await IncrByFloatAsync(state, args, ct).ConfigureAwait(false);
                default: return RespWire.Error("ERR unknown command '" + args[0] + "'");
            }
        }

        #endregion

        #region Private-Methods-Connection-Commands

        private static byte[] Hello(List<string> args, RespConnectionState state)
        {
            if (args.Count > 1)
            {
                if (args[1] == "3") state.Resp3 = true;
                else if (args[1] == "2") state.Resp3 = false;
                else return RespWire.Error("NOPROTO unsupported protocol version");
            }

            List<byte[]> pairs = new List<byte[]>
            {
                RespWire.BulkString("server"), RespWire.BulkString("pepperx"),
                RespWire.BulkString("version"), RespWire.BulkString(Constants.ProductVersion),
                RespWire.BulkString("proto"), RespWire.Integer(state.Resp3 ? 3 : 2),
                RespWire.BulkString("id"), RespWire.Integer(1),
                RespWire.BulkString("mode"), RespWire.BulkString("standalone"),
                RespWire.BulkString("role"), RespWire.BulkString("master"),
                RespWire.BulkString("modules"), RespWire.Array(new List<byte[]>())
            };
            return RespWire.Map(pairs, state.Resp3);
        }

        private byte[] Select(List<string> args, RespConnectionState state)
        {
            if (args.Count < 2 || !int.TryParse(args[1], out int index)) return RespWire.Error("ERR invalid DB index");
            if (index < 0 || index >= _Settings.DatabaseCount) return RespWire.Error("ERR DB index is out of range");
            state.DatabaseIndex = index;
            return RespWire.SimpleString("OK");
        }

        private static byte[] Client(List<string> args, RespConnectionState state)
        {
            if (args.Count < 2) return RespWire.Error("ERR wrong number of arguments for 'client'");
            string sub = args[1].ToUpperInvariant();
            switch (sub)
            {
                case "SETNAME": state.Name = args.Count > 2 ? args[2] : string.Empty; return RespWire.SimpleString("OK");
                case "GETNAME": return RespWire.BulkString(state.Name);
                case "ID": return RespWire.Integer(1);
                default: return RespWire.SimpleString("OK");
            }
        }

        private static byte[] Command(List<string> args)
        {
            if (args.Count > 1 && args[1].ToUpperInvariant() == "COUNT") return RespWire.Integer(30);
            return RespWire.Array(new List<byte[]>());
        }

        private static byte[] Config(List<string> args)
        {
            if (args.Count > 1 && args[1].ToUpperInvariant() == "GET") return RespWire.Array(new List<byte[]>());
            return RespWire.SimpleString("OK");
        }

        private byte[] Info(RespConnectionState state)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("# Server\r\n");
            sb.Append("redis_version:7.0.0-pepperx-" + Constants.ProductVersion + "\r\n");
            sb.Append("redis_mode:standalone\r\n");
            sb.Append("# Keyspace\r\n");
            sb.Append("db" + state.DatabaseIndex + ":keys=0,expires=0,avg_ttl=0\r\n");
            return RespWire.BulkString(sb.ToString());
        }

        #endregion

        #region Private-Methods-Data-Commands

        private async Task<byte[]> GetAsync(RespConnectionState state, List<string> args, CancellationToken ct)
        {
            if (args.Count < 2) return RespWire.Error("ERR wrong number of arguments for 'get'");
            byte[]? value = await ReadValueAsync(state, args[1], ct).ConfigureAwait(false);
            return value == null ? RespWire.Null(state.Resp3) : RespWire.BulkString(value);
        }

        private async Task<byte[]> SetAsync(RespConnectionState state, List<string> args, CancellationToken ct)
        {
            if (args.Count < 3) return RespWire.Error("ERR wrong number of arguments for 'set'");

            bool nx = false;
            bool xx = false;
            for (int i = 3; i < args.Count; i++)
            {
                string opt = args[i].ToUpperInvariant();
                if (opt == "NX") nx = true;
                else if (opt == "XX") xx = true;
                else return RespWire.Error("ERR the SET option '" + args[i] + "' is not supported by PepperX");
            }

            bool set = await WriteValueAsync(state, args[1], RespWire.ToBytes(args[2]), nx, xx, ct).ConfigureAwait(false);
            if (!set) return RespWire.Null(state.Resp3);
            return RespWire.SimpleString("OK");
        }

        private async Task<byte[]> SetNxAsync(RespConnectionState state, List<string> args, CancellationToken ct)
        {
            if (args.Count < 3) return RespWire.Error("ERR wrong number of arguments for 'setnx'");
            bool set = await WriteValueAsync(state, args[1], RespWire.ToBytes(args[2]), true, false, ct).ConfigureAwait(false);
            return RespWire.Integer(set ? 1 : 0);
        }

        private async Task<byte[]> GetSetAsync(RespConnectionState state, List<string> args, CancellationToken ct)
        {
            if (args.Count < 3) return RespWire.Error("ERR wrong number of arguments for 'getset'");
            byte[]? old = await ReadValueAsync(state, args[1], ct).ConfigureAwait(false);
            await WriteValueAsync(state, args[1], RespWire.ToBytes(args[2]), false, false, ct).ConfigureAwait(false);
            return old == null ? RespWire.Null(state.Resp3) : RespWire.BulkString(old);
        }

        private async Task<byte[]> GetDelAsync(RespConnectionState state, List<string> args, CancellationToken ct)
        {
            if (args.Count < 2) return RespWire.Error("ERR wrong number of arguments for 'getdel'");
            byte[]? old = await ReadValueAsync(state, args[1], ct).ConfigureAwait(false);
            if (old == null) return RespWire.Null(state.Resp3);
            await DeleteValueAsync(state, args[1], ct).ConfigureAwait(false);
            return RespWire.BulkString(old);
        }

        private async Task<byte[]> MGetAsync(RespConnectionState state, List<string> args, CancellationToken ct)
        {
            List<byte[]> elements = new List<byte[]>();
            for (int i = 1; i < args.Count; i++)
            {
                byte[]? value = await ReadValueAsync(state, args[i], ct).ConfigureAwait(false);
                elements.Add(value == null ? RespWire.Null(state.Resp3) : RespWire.BulkString(value));
            }
            return RespWire.Array(elements);
        }

        private async Task<byte[]> MSetAsync(RespConnectionState state, List<string> args, CancellationToken ct)
        {
            if (args.Count < 3 || (args.Count - 1) % 2 != 0) return RespWire.Error("ERR wrong number of arguments for 'mset'");
            for (int i = 1; i + 1 < args.Count; i += 2)
            {
                await WriteValueAsync(state, args[i], RespWire.ToBytes(args[i + 1]), false, false, ct).ConfigureAwait(false);
            }
            return RespWire.SimpleString("OK");
        }

        private async Task<byte[]> DelAsync(RespConnectionState state, List<string> args, CancellationToken ct)
        {
            long deleted = 0;
            for (int i = 1; i < args.Count; i++)
            {
                if (await DeleteValueAsync(state, args[i], ct).ConfigureAwait(false)) deleted++;
            }
            return RespWire.Integer(deleted);
        }

        private async Task<byte[]> ExistsAsync(RespConnectionState state, List<string> args, CancellationToken ct)
        {
            long count = 0;
            string container = ContainerName(state);
            for (int i = 1; i < args.Count; i++)
            {
                if (await _Reads.ExistsAsync(container, args[i], ct).ConfigureAwait(false)) count++;
            }
            return RespWire.Integer(count);
        }

        private async Task<byte[]> StrLenAsync(RespConnectionState state, List<string> args, CancellationToken ct)
        {
            if (args.Count < 2) return RespWire.Error("ERR wrong number of arguments for 'strlen'");
            ObjectMetadata? meta = await _Reads.ReadMetadataAsync(ContainerName(state), args[1], ct).ConfigureAwait(false);
            return RespWire.Integer(meta?.SizeBytes ?? 0);
        }

        private async Task<byte[]> TypeAsync(RespConnectionState state, List<string> args, CancellationToken ct)
        {
            if (args.Count < 2) return RespWire.Error("ERR wrong number of arguments for 'type'");
            bool exists = await _Reads.ExistsAsync(ContainerName(state), args[1], ct).ConfigureAwait(false);
            return RespWire.SimpleString(exists ? "string" : "none");
        }

        private async Task<byte[]> TtlAsync(RespConnectionState state, List<string> args, CancellationToken ct)
        {
            if (args.Count < 2) return RespWire.Error("ERR wrong number of arguments for 'ttl'");
            bool exists = await _Reads.ExistsAsync(ContainerName(state), args[1], ct).ConfigureAwait(false);
            return RespWire.Integer(exists ? -1 : -2);
        }

        private async Task<byte[]> KeysAsync(RespConnectionState state, List<string> args, CancellationToken ct)
        {
            string pattern = args.Count > 1 ? args[1] : "*";
            List<byte[]> matches = new List<byte[]>();
            foreach (string key in await ListKeysAsync(state, ct).ConfigureAwait(false))
            {
                if (GlobMatch(pattern, key)) matches.Add(RespWire.BulkString(key));
            }
            return RespWire.Array(matches);
        }

        private async Task<byte[]> ScanAsync(RespConnectionState state, List<string> args, CancellationToken ct)
        {
            int cursor = args.Count > 1 && int.TryParse(args[1], out int c) ? c : 0;
            string pattern = "*";
            int count = 100;
            for (int i = 2; i + 1 < args.Count; i += 2)
            {
                string opt = args[i].ToUpperInvariant();
                if (opt == "MATCH") pattern = args[i + 1];
                else if (opt == "COUNT" && int.TryParse(args[i + 1], out int n)) count = n;
            }

            List<string> all = await ListKeysAsync(state, ct).ConfigureAwait(false);
            List<byte[]> page = new List<byte[]>();
            int next = 0;
            for (int i = cursor; i < all.Count && page.Count < count; i++)
            {
                if (GlobMatch(pattern, all[i])) page.Add(RespWire.BulkString(all[i]));
                next = i + 1;
            }
            if (next >= all.Count) next = 0;

            List<byte[]> result = new List<byte[]> { RespWire.BulkString(next.ToString(CultureInfo.InvariantCulture)), RespWire.Array(page) };
            return RespWire.Array(result);
        }

        private async Task<byte[]> IncrByAsync(RespConnectionState state, string key, long delta, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(key)) return RespWire.Error("ERR wrong number of arguments");

            string container = ContainerName(state);
            SemaphoreSlim gate = _KeyLocks.GetOrAdd(container + "\n" + key, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                byte[]? current = await ReadValueAsync(state, key, ct).ConfigureAwait(false);
                long value = 0;
                if (current != null)
                {
                    string text = Encoding.Latin1.GetString(current);
                    if (!long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                        return RespWire.Error("ERR value is not an integer or out of range");
                }
                value += delta;
                await WriteValueAsync(state, key, RespWire.ToBytes(value.ToString(CultureInfo.InvariantCulture)), false, false, ct).ConfigureAwait(false);
                return RespWire.Integer(value);
            }
            finally
            {
                gate.Release();
            }
        }

        private async Task<byte[]> IncrByFloatAsync(RespConnectionState state, List<string> args, CancellationToken ct)
        {
            if (args.Count < 3) return RespWire.Error("ERR wrong number of arguments for 'incrbyfloat'");
            string key = args[1];
            if (!double.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double delta))
                return RespWire.Error("ERR value is not a valid float");

            string container = ContainerName(state);
            SemaphoreSlim gate = _KeyLocks.GetOrAdd(container + "\n" + key, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                byte[]? current = await ReadValueAsync(state, key, ct).ConfigureAwait(false);
                double value = 0;
                if (current != null)
                {
                    if (!double.TryParse(Encoding.Latin1.GetString(current), NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                        return RespWire.Error("ERR value is not a valid float");
                }
                value += delta;
                string text = value.ToString("R", CultureInfo.InvariantCulture);
                await WriteValueAsync(state, key, RespWire.ToBytes(text), false, false, ct).ConfigureAwait(false);
                return RespWire.BulkString(text);
            }
            finally
            {
                gate.Release();
            }
        }

        #endregion

        #region Private-Methods-Helpers

        private string ContainerName(RespConnectionState state)
        {
            return _Settings.ContainerPrefix + state.DatabaseIndex;
        }

        private async Task EnsureContainerAsync(RespConnectionState state, CancellationToken ct)
        {
            string name = ContainerName(state);
            if (await _Containers.ExistsAsync(name, ct).ConfigureAwait(false)) return;
            try
            {
                await _Containers.CreateAsync(new ContainerCreateRequest { Name = name }, ct).ConfigureAwait(false);
            }
            catch (PepperXException)
            {
                // Concurrent creation; ignore.
            }
        }

        private async Task<byte[]?> ReadValueAsync(RespConnectionState state, string key, CancellationToken ct)
        {
            try
            {
                await using (ObjectReadHandle? handle = await _Reads.ReadAsync(ContainerName(state), key, null, null, ct).ConfigureAwait(false))
                {
                    if (handle == null) return null;
                    using (MemoryStream ms = new MemoryStream())
                    {
                        await handle.Payload.CopyToAsync(ms, ct).ConfigureAwait(false);
                        return ms.ToArray();
                    }
                }
            }
            catch (ContainerNotFoundException)
            {
                return null;
            }
        }

        private async Task<bool> WriteValueAsync(RespConnectionState state, string key, byte[] value, bool nx, bool xx, CancellationToken ct)
        {
            await EnsureContainerAsync(state, ct).ConfigureAwait(false);
            string container = ContainerName(state);

            if (xx && !await _Reads.ExistsAsync(container, key, ct).ConfigureAwait(false)) return false;

            try
            {
                using (MemoryStream ms = new MemoryStream(value))
                {
                    await _Writes.WriteAsync(container, key, ms, Constants.OctetStreamContentType, null, null, null, nx, ct).ConfigureAwait(false);
                }
                return true;
            }
            catch (ObjectAlreadyExistsException)
            {
                return false;
            }
        }

        private async Task<bool> DeleteValueAsync(RespConnectionState state, string key, CancellationToken ct)
        {
            try
            {
                return await _Deletes.DeleteAsync(ContainerName(state), key, ct).ConfigureAwait(false);
            }
            catch (ContainerNotFoundException)
            {
                return false;
            }
        }

        private async Task<long> CountAsync(RespConnectionState state, CancellationToken ct)
        {
            Container? container = await _Db.Containers.ReadByNameAsync(ContainerName(state), ct).ConfigureAwait(false);
            if (container == null) return 0;
            return await _Db.Extents.CountActiveAsync(container.Id, ct).ConfigureAwait(false);
        }

        private async Task<byte[]> FlushDbAsync(RespConnectionState state, CancellationToken ct)
        {
            Container? container = await _Db.Containers.ReadByNameAsync(ContainerName(state), ct).ConfigureAwait(false);
            if (container != null) await _Deletes.BulkDeleteContainerAsync(container.Id, ct).ConfigureAwait(false);
            return RespWire.SimpleString("OK");
        }

        private async Task<List<string>> ListKeysAsync(RespConnectionState state, CancellationToken ct)
        {
            List<string> keys = new List<string>();
            Container? container = await _Db.Containers.ReadByNameAsync(ContainerName(state), ct).ConfigureAwait(false);
            if (container == null) return keys;

            string? continuation = null;
            while (true)
            {
                EnumerationQuery query = new EnumerationQuery { MaxResults = 500, Ordering = EnumerationOrderEnum.CreatedAscending, ContinuationToken = continuation };
                EnumerationResult<Extent> page = await _Db.Extents.EnumerateAsync(container.Id, query, ct).ConfigureAwait(false);
                foreach (Extent extent in page.Objects) keys.Add(extent.Key);
                if (page.EndOfResults || page.ContinuationToken == null) break;
                continuation = page.ContinuationToken;
            }

            return keys;
        }

        private static List<string> ExtractArgs(object? value)
        {
            List<string> args = new List<string>();
            if (value is object[] array)
            {
                foreach (object? element in array) args.Add(element?.ToString() ?? string.Empty);
            }
            else if (value is System.Collections.IEnumerable enumerable && value is not string)
            {
                foreach (object? element in enumerable) args.Add(element?.ToString() ?? string.Empty);
            }
            return args;
        }

        private static long ParseLong(List<string> args, int index, long fallback)
        {
            if (index < args.Count && long.TryParse(args[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out long value)) return value;
            return fallback;
        }

        private static bool GlobMatch(string pattern, string text)
        {
            if (pattern == "*") return true;
            return GlobMatchAt(pattern, 0, text, 0);
        }

        private static bool GlobMatchAt(string pattern, int p, string text, int t)
        {
            while (p < pattern.Length)
            {
                char pc = pattern[p];
                if (pc == '*')
                {
                    while (p < pattern.Length && pattern[p] == '*') p++;
                    if (p == pattern.Length) return true;
                    for (int i = t; i <= text.Length; i++)
                    {
                        if (GlobMatchAt(pattern, p, text, i)) return true;
                    }
                    return false;
                }
                if (t >= text.Length) return false;
                if (pc == '?' || pc == text[t]) { p++; t++; continue; }
                return false;
            }
            return t == text.Length;
        }

        private void SendToClient(Guid clientGuid, byte[] response)
        {
            try
            {
                ClientInfo? info = _Listener?.RetrieveClientByGuid(clientGuid);
                TcpClient? client = info?.TcpClient;
                if (client == null || !client.Connected) return;

                NetworkStream stream = client.GetStream();
                lock (client)
                {
                    stream.Write(response, 0, response.Length);
                    stream.Flush();
                }
            }
            catch (Exception ex)
            {
                _Logging?.Debug("[RespProtocolHandler] send failed: " + ex.Message);
            }
        }

        #endregion
    }
}
