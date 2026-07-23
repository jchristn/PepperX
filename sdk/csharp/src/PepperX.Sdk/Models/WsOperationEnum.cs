namespace PepperX.Sdk.Models
{
    /// <summary>
    /// Operations supported by the PepperX WebSocket surface.
    /// </summary>
    public enum WsOperationEnum
    {
        /// <summary>Health check.</summary>
        Health,

        /// <summary>Create a container.</summary>
        ContainerCreate,

        /// <summary>Read a container.</summary>
        ContainerRead,

        /// <summary>List containers.</summary>
        ContainerList,

        /// <summary>Enumerate containers.</summary>
        ContainerEnumerate,

        /// <summary>Update a container's tags.</summary>
        ContainerUpdateTags,

        /// <summary>Delete a container.</summary>
        ContainerDelete,

        /// <summary>Check container existence.</summary>
        ContainerExists,

        /// <summary>Write an object.</summary>
        ObjectWrite,

        /// <summary>Read an object payload.</summary>
        ObjectRead,

        /// <summary>Read an object's metadata.</summary>
        ObjectReadMetadata,

        /// <summary>Update an object's metadata.</summary>
        ObjectUpdateMetadata,

        /// <summary>Delete an object.</summary>
        ObjectDelete,

        /// <summary>Check object existence.</summary>
        ObjectExists,

        /// <summary>List objects in a container.</summary>
        ObjectList,

        /// <summary>Enumerate objects in a container.</summary>
        ObjectEnumerate,

        /// <summary>Search objects across containers.</summary>
        SearchEnumerate,

        /// <summary>Aggregate statistics.</summary>
        AdminStats,

        /// <summary>List cluster nodes.</summary>
        AdminNodes
    }
}
