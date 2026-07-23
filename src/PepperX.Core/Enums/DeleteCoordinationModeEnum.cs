namespace PepperX.Core.Enums
{
    /// <summary>
    /// Strategy used to guarantee that a delete waits for in-flight reads to finish.
    /// </summary>
    public enum DeleteCoordinationModeEnum
    {
        /// <summary>
        /// Cluster-wide coordination through database read leases. A delete provably waits for every
        /// in-flight read on every node. This is the default and the only correct choice for multi-node
        /// deployments.
        /// </summary>
        Cluster,

        /// <summary>
        /// In-process coordination only, using a per-extent reader/writer lock. Lower overhead but safe
        /// only for single-node deployments; multi-node deletes may race reads on other nodes.
        /// </summary>
        Local
    }
}
