namespace CrmLeadManagement.Models;

/// <summary>Small formatting helpers shared by the sidebar Finance views (Estimation/Quotation/Proposal).</summary>
public static class FinanceHelpers
{
    public static string StatusBadge(string status)
    {
        var cls = (status ?? "").ToLower().Replace(" ", "") switch
        {
            "draft" => "fst-draft",
            "sent" => "fst-sent",
            "approved" => "fst-approved",
            "rejected" => "fst-rejected",
            "expired" => "fst-expired",
            _ => "fst-draft"
        };
        var label = string.IsNullOrWhiteSpace(status) ? "Draft" : status;
        return $"<span class='badge-status {cls}'>{label}</span>";
    }

    public static string Money(string? amount)
    {
        if (decimal.TryParse(amount, out var v))
        {
            return "₹" + v.ToString("N2");
        }
        return string.IsNullOrWhiteSpace(amount) ? "₹0.00" : amount!;
    }

    public static string SumTotals(IEnumerable<string> amounts)
    {
        decimal sum = 0;
        foreach (var a in amounts)
        {
            if (decimal.TryParse(a, out var v)) sum += v;
        }
        return "₹" + sum.ToString("N0");
    }
}
