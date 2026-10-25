namespace ReStore.Domain.Common
{
    /// <summary>
    /// Marker base class for every aggregate root the generic repositories are
    /// allowed to handle.
    ///
    /// It is deliberately empty. The key property is discovered from EF Core
    /// metadata in ReStore.Infrastructure (see GenericQueryRepositoryBase), so
    /// entities need no [Key] attribute and this project needs no reference to a
    /// persistence framework. That is the whole reason [Owned] was removed from
    /// ShippingAddress and ProductItemOrdered in Phase 0.
    /// </summary>
    public abstract class BaseEntity
    {
    }
}
