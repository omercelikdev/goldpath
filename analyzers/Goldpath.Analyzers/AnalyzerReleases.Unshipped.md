; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
GP0404 | Goldpath | Warning | Publish through IIntegrationEventPublisher, not MassTransit's IPublishEndpoint
GP0405 | Goldpath | Info | Consume through IIntegrationEventHandler + AddGoldpathHandler, not MassTransit's IConsumer (Warning at the next train boundary)
GP2001 | Goldpath | Warning | ProductDeclaresGoldpathNamespaceAnalyzer, [Documentation](https://github.com/omercelikdev/goldpath/blob/main/docs/rfc/goldpath-platform-sdk.md)
