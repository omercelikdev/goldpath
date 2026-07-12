# Trace correlation: following one instruction end to end

Goldpath's batch work happens on the far side of a scheduler: an upload returns 202 long
before any row executes, and the execute run may fire hours later on another node. Plain
parent/child tracing dies at that boundary — `Activity.Current` cannot cross a Quartz
store. This document explains the correlation model that survives it, and how to walk it
in your trace backend (Tempo, Jaeger, any OTLP consumer).

## The model: roots plus links

Every **fire** (cron, trigger verb, replay verb) starts its own trace, rooted at a
`goldpath.job.run` (or `goldpath.job.replay`) span. What ties traces together is **span
links**, recorded at two seams:

| Seam | Carrier | Written by | Linked from |
|---|---|---|---|
| Request → fire | `goldpath:traceparent` in the Quartz job data map | the `trigger` / `replay-items` admin verbs | the run/replay span of the fire it caused |
| Upload → batch work | `TraceParent` column on the bulk batch | ingest (upload verb) | every `goldpath.bulk.validate` / `.execute-range` / `.replay-row` span over that batch |

Inside one fire, everything is a normal tree — one trace id:

```
goldpath.job.run  (root; links → the operator's trigger trace, when one caused the fire)
└─ goldpath.job.chunk           tags: run_id, chunk, attempt
   └─ goldpath.bulk.execute-range   tags: batch_id, range; links → the UPLOAD trace
      └─ (your row handler's spans: HttpClient to core banking, etc. — inherited free)
```

The replay path mirrors it:

```
goldpath.job.replay  (root; links → the operator's replay-items request trace)
└─ goldpath.job.replay-item     tags: run_id, item_key      ← the instruction's coordinate
   └─ goldpath.bulk.replay-row  tags: batch_id, row; links → the UPLOAD trace
```

## Walking a payment (the finance question)

"Where is instruction E42 of Monday's file?"

1. Find the upload: search the API's server spans for the upload request (or take the
   batch's `TraceParent` column directly — the batch id is on every admin screen).
2. Follow the **incoming links** to that trace: the validate run, then the execute-range
   span covering the row's range. Its trace id is the run's trace — every retry, every
   downstream call of that chunk sits under it.
3. If the row failed, the repair queue holds its `item_key` (`bulk:<batchId>:<row>`).
   After `replay-items`, search spans tagged `goldpath.item_key = <that key>`: the
   replay-item span carries the operator's trace, and its `goldpath.bulk.replay-row`
   child links back to the original upload — both directions stay navigable.

Grafana Tempo: enable the trace-to-trace link panel on span links (`Links` tab on any
span); TraceQL example over tags:

```
{ span.goldpath.batch_id = "<batch guid>" }
{ span.goldpath.item_key = "bulk:<batch>:<row>" }
```

## What ships where

- **ServiceDefaults** subscribes `AddSource("Goldpath.*")` (and `MassTransit`) — without
  it every module span is a silent no-op; the subscription test guards this the same way
  the meter-export test guards the boards.
- **Sampling**: run/chunk spans are roots, so `ParentBasedSampler` treats each fire
  independently at your configured ratio; `Observability.Profile = Full` records all.
- **Schema**: the batch `TraceParent` column arrives via normal migrations
  (`goldpath db add <Name>` after upgrading — see the migrations runbook).

## Boundaries (deliberate)

- **No per-row spans during bulk execution.** A million-row batch must not emit a
  million spans; the range span (bounded by `ChunkSize`) is the unit. Per-row precision
  exists exactly where it pays: the repair/replay path, which is low-volume by
  definition.
- **Cron fires with no cause carry no link.** A scheduled run's trace stands alone; the
  batch-level links still anchor its bulk work to the upload.
