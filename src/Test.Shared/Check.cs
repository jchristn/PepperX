namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;

    /// <summary>
    /// Minimal assertion helpers for Touchstone descriptors. Failures throw; there is no console output.
    /// </summary>
    public static class Check
    {
        /// <summary>
        /// Assert a condition is true.
        /// </summary>
        /// <param name="condition">Condition to assert.</param>
        /// <param name="message">Failure message.</param>
        public static void True(bool condition, string message)
        {
            if (!condition) throw new Exception("Assertion failed: " + message);
        }

        /// <summary>
        /// Assert a condition is false.
        /// </summary>
        /// <param name="condition">Condition to assert.</param>
        /// <param name="message">Failure message.</param>
        public static void False(bool condition, string message)
        {
            if (condition) throw new Exception("Assertion failed: " + message);
        }

        /// <summary>
        /// Assert two values are equal.
        /// </summary>
        /// <typeparam name="T">Value type.</typeparam>
        /// <param name="expected">Expected value.</param>
        /// <param name="actual">Actual value.</param>
        /// <param name="message">Context for the failure message.</param>
        public static void Equal<T>(T expected, T actual, string message)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new Exception("Assertion failed: " + message + " (expected '" + expected + "', got '" + actual + "')");
        }

        /// <summary>
        /// Assert a value is not null.
        /// </summary>
        /// <param name="value">Value to check.</param>
        /// <param name="message">Failure message.</param>
        public static void NotNull(object? value, string message)
        {
            if (value == null) throw new Exception("Assertion failed: " + message + " (was null)");
        }

        /// <summary>
        /// Assert that an action throws an exception of type <typeparamref name="T"/>.
        /// </summary>
        /// <typeparam name="T">Expected exception type.</typeparam>
        /// <param name="action">Action expected to throw.</param>
        /// <param name="message">Context for the failure message.</param>
        public static void Throws<T>(Action action, string message) where T : Exception
        {
            try
            {
                action();
            }
            catch (T)
            {
                return;
            }
            catch (Exception ex)
            {
                throw new Exception("Assertion failed: " + message + " (expected " + typeof(T).Name + ", got " + ex.GetType().Name + ")");
            }

            throw new Exception("Assertion failed: " + message + " (expected " + typeof(T).Name + ", nothing thrown)");
        }

        /// <summary>
        /// Assert that an async action throws an exception of type <typeparamref name="T"/>.
        /// </summary>
        /// <typeparam name="T">Expected exception type.</typeparam>
        /// <param name="action">Async action expected to throw.</param>
        /// <param name="message">Context for the failure message.</param>
        /// <returns>Task.</returns>
        public static async Task ThrowsAsync<T>(Func<Task> action, string message) where T : Exception
        {
            try
            {
                await action().ConfigureAwait(false);
            }
            catch (T)
            {
                return;
            }
            catch (Exception ex)
            {
                throw new Exception("Assertion failed: " + message + " (expected " + typeof(T).Name + ", got " + ex.GetType().Name + ")");
            }

            throw new Exception("Assertion failed: " + message + " (expected " + typeof(T).Name + ", nothing thrown)");
        }
    }
}
