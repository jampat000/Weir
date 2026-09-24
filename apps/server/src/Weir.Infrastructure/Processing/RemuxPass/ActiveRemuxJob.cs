namespace Weir.Infrastructure.Processing.RemuxPass;

/// <summary>A pending or leased remux job, as the cleanup gates read it.</summary>
public sealed record ActiveRemuxJob(long Id, string? PayloadJson);
