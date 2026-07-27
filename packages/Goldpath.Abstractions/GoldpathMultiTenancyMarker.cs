namespace Goldpath;

/// <summary>
/// The app-level answer to "is THIS application multi-tenant?" — registered by
/// <c>AddGoldpathMultiTenancy</c> and by nothing else.
/// <para>
/// The admin surfaces (contract revision R1) must scope every read and verb to the ambient
/// tenant on a multi-tenant app, and keep the pre-R1 semantics on a single-tenant one. The
/// presence of an <see cref="ITenantContext"/> cannot answer that question: other modules
/// register one for their own flow (messaging propagates the tenant of a consumed message),
/// so a single-tenant app that merely composes a broker would otherwise have its admin
/// surfaces refuse every request — 400 "no ambient tenant", or 403 on the surfaces whose
/// rows carry no tenant column at all.
/// </para>
/// </summary>
public sealed class GoldpathMultiTenancyMarker;
