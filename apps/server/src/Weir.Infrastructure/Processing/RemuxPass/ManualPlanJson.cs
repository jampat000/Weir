using Weir.Core.Json;
using Weir.Core.Processing;

namespace Weir.Infrastructure.Processing.RemuxPass;

/// <summary>
/// JSON (de)serialization for a manual track plan and the source fingerprint recorded when it was chosen (issue #501),
/// both stored on the enqueued <c>processing.file.remux_pass.v1</c> job payload as <c>manual_plan</c> and
/// <c>source_fingerprint</c>.
/// </summary>
public static class ManualPlanJson
{
    public static WireObject ToPyDict(ManualPlanChoice choice)
    {
        ArgumentNullException.ThrowIfNull(choice);
        return new WireObject()
            .Set("keep", new WireArray(choice.Keep.Select(e => (WireValue)new WireObject()
                .Set("index", e.Index)
                .Set("default", e.Default)
                .Set("forced", e.Forced))))
            .Set("order", new WireArray(choice.Order.Select(i => (WireValue)new WireInteger(i))));
    }

    /// <summary>Null when the payload's <c>manual_plan</c> is missing or not shaped as expected.</summary>
    public static ManualPlanChoice? FromPyJson(WireValue? value)
    {
        if (value is not WireObject dict || dict.Get("keep") is not WireArray keepList)
        {
            return null;
        }

        var keep = new List<ManualKeepEntry>();
        foreach (var item in keepList.Items)
        {
            if (item is not WireObject entry || entry.Get("index") is not WireInteger index)
            {
                return null;
            }

            var isDefault = entry.Get("default") is WireBool { Value: true };
            var forced = entry.Get("forced") is WireBool { Value: true };
            keep.Add(new ManualKeepEntry((int)index.Value, isDefault, forced));
        }

        var order = new List<int>();
        if (dict.Get("order") is WireArray orderList)
        {
            foreach (var item in orderList.Items)
            {
                if (item is WireInteger orderIndex)
                {
                    order.Add((int)orderIndex.Value);
                }
            }
        }

        return new ManualPlanChoice(keep, order);
    }

    public static WireObject ToPyDict(SourceFingerprint fingerprint) => new WireObject()
        .Set("device", new WireInteger(fingerprint.Device))
        .Set("inode", new WireInteger(fingerprint.Inode))
        .Set("size_bytes", fingerprint.SizeBytes)
        .Set("modified_time_ns", fingerprint.ModifiedTimeNs);

    /// <summary>Null when the payload's <c>source_fingerprint</c> is missing or not shaped as expected.</summary>
    public static SourceFingerprint? FingerprintFromPyJson(WireValue? value)
    {
        if (value is not WireObject dict
            || dict.Get("device") is not WireInteger device
            || dict.Get("inode") is not WireInteger inode
            || dict.Get("size_bytes") is not WireInteger sizeBytes
            || dict.Get("modified_time_ns") is not WireInteger modifiedTimeNs)
        {
            return null;
        }

        return new SourceFingerprint((ulong)device.Value, (ulong)inode.Value, (long)sizeBytes.Value, (long)modifiedTimeNs.Value);
    }
}
