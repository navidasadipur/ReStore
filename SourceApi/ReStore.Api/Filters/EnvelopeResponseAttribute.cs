namespace ReStore.Api.Filters;

// Opt in explicitly: the educational error endpoints and provider webhooks keep
// their HTTP status contracts even though they share the same controller base.
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true)]
public sealed class EnvelopeResponseAttribute : Attribute;
