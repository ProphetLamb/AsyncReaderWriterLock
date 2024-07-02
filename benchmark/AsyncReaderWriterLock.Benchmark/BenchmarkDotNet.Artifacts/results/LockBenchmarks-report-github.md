```

BenchmarkDotNet v0.13.12, CachyOS
AMD Ryzen 5 5500U with Radeon Graphics, 1 CPU, 12 logical and 6 physical cores
.NET SDK 8.0.302
  [Host] : .NET 8.0.6 (8.0.624.26715), X64 RyuJIT AVX2


```
| Method    | Iterations | ReadWeight | ReadUpgradableWeight | WriteUpgradedWeight | Mean | Error |
|---------- |----------- |----------- |--------------------- |-------------------- |-----:|------:|
| ARWLAsync | 1000000    | 0          | 0                    | 0                   |   NA |    NA |

Benchmarks with issues:
  LockBenchmarks.ARWLAsync: DefaultJob [Iterations=1000000, ReadWeight=0, ReadUpgradableWeight=0, WriteUpgradedWeight=0]
