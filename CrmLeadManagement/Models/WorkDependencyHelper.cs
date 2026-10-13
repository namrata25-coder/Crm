namespace CrmLeadManagement.Models;

public class MilestoneDependencyInfo
{
    public int SequenceIndex { get; set; } = 1;
    public int TotalMilestones { get; set; } = 1;
    public string DependencyType { get; set; } = "Strict Finish-to-Start (Sequential with 100% Completion & Full Invoice Paid Requirement)";
    public bool IsLocked { get; set; } = false;
    public string LockReason { get; set; } = "";
    public int? PrerequisiteMilestoneId { get; set; }
    public string? PrerequisiteMilestoneCode { get; set; } = "";
    public string? PrerequisiteMilestoneName { get; set; } = "";
    public string? PrerequisiteStatus { get; set; } = "";
    public bool PrerequisiteCompleted { get; set; } = true;
    public bool PrerequisiteInvoiced { get; set; } = true;
    public string? PrerequisiteInvoiceNumber { get; set; } = "";
    public bool PrerequisitePaid { get; set; } = true;
    public string? PrerequisiteInvoiceStatus { get; set; } = "";
    public decimal? PrerequisiteGrandTotal { get; set; }
    public decimal? PrerequisitePaidAmount { get; set; }
}

public class TaskDependencyInfo
{
    public int SequenceIndex { get; set; } = 1;
    public int TotalTasks { get; set; } = 1;
    public string DependencyType { get; set; } = "Strict Finish-to-Start Sequential Task Dependency";
    public bool IsLocked { get; set; } = false;
    public string LockReason { get; set; } = "";
    public int? PrerequisiteTaskId { get; set; }
    public string? PrerequisiteTaskCode { get; set; } = "";
    public string? PrerequisiteTaskName { get; set; } = "";
    public string? PrerequisiteStatus { get; set; } = "";
    public bool PrerequisiteCompleted { get; set; } = true;
    public bool ParentMilestoneLocked { get; set; } = false;
    public string? ParentMilestoneCode { get; set; } = "";
    public bool HasExplicitDependency { get; set; } = false;
}

public static class WorkDependencyHelper
{
    /// <summary>
    /// Parses stored dependency markers like [DEP:123] or [NO_DEP] from task notes.
    /// </summary>
    public static (bool HasDependency, int? DependencyTaskId, string CleanNotes) ParseTaskDependencyNotes(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes))
            return (false, null, "");

        var raw = notes.Trim();
        if (raw.Contains("[NO_DEP]"))
        {
            var clean = raw.Replace("[NO_DEP]", "").Trim();
            return (false, null, clean);
        }

        var match = System.Text.RegularExpressions.Regex.Match(raw, @"\[DEP:(\d+)\]");
        if (match.Success && int.TryParse(match.Groups[1].Value, out var depId))
        {
            var clean = raw.Replace(match.Value, "").Trim();
            return (true, depId, clean);
        }

        return (false, null, raw);
    }

    /// <summary>
    /// Appends the clean dependency marker [DEP:123] or [NO_DEP] into notes string for storage.
    /// </summary>
    public static string FormatTaskNotesWithDependency(string? cleanNotes, bool hasDependency, int? dependencyTaskId)
    {
        var text = cleanNotes?.Trim() ?? "";
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\[DEP:\d+\]", "").Replace("[NO_DEP]", "").Trim();

        if (hasDependency && dependencyTaskId.HasValue && dependencyTaskId.Value > 0)
        {
            return string.IsNullOrEmpty(text) ? $"[DEP:{dependencyTaskId.Value}]" : $"{text}\n[DEP:{dependencyTaskId.Value}]";
        }
        else
        {
            return string.IsNullOrEmpty(text) ? "[NO_DEP]" : $"{text}\n[NO_DEP]";
        }
    }

    /// <summary>
    /// Calculates the sequential dependency and lock state for a milestone within a project.
    /// Condition: Milestone N cannot move/start until Milestone N-1 is fully completed AND its complete invoice is generated and paid.
    /// </summary>
    public static MilestoneDependencyInfo EvaluateMilestone(
        WorkMilestone milestone,
        IEnumerable<WorkMilestone> allMilestonesInProject,
        IEnumerable<WorkInvoice> projectInvoices)
    {
        var ordered = allMilestonesInProject
            .OrderBy(m => m.Id)
            .ToList();

        var index = ordered.FindIndex(m => m.Id == milestone.Id);
        if (index < 0)
        {
            // Milestone might be new or not in list yet
            index = ordered.Count; // Will be the next in sequence
        }

        var seq = index + 1;
        var info = new MilestoneDependencyInfo
        {
            SequenceIndex = seq,
            TotalMilestones = Math.Max(ordered.Count, seq),
            DependencyType = "Strict Finish-to-Start (Sequential with 100% Completion & Full Invoice Paid Requirement)"
        };

        if (index == 0)
        {
            // Initial Milestone (Milestone 1) is unlocked by default
            info.IsLocked = false;
            info.LockReason = "";
            info.PrerequisiteCompleted = true;
            info.PrerequisiteInvoiced = true;
            info.PrerequisitePaid = true;
            return info;
        }

        // Must check previous milestone
        var prev = ordered[index - 1];
        info.PrerequisiteMilestoneId = prev.Id;
        info.PrerequisiteMilestoneCode = prev.MilestoneCode;
        info.PrerequisiteMilestoneName = prev.MilestoneName;
        info.PrerequisiteStatus = prev.Status;

        var isCompleted = string.Equals(prev.Status, "Completed", StringComparison.OrdinalIgnoreCase) ||
                          (prev.Amount > 0 && prev.CompletedAmount >= prev.Amount) ||
                          prev.ProgressPercent >= 100;
        info.PrerequisiteCompleted = isCompleted;

        var inv = projectInvoices.FirstOrDefault(i => i.MilestoneId == prev.Id);
        info.PrerequisiteInvoiced = inv != null;
        info.PrerequisiteInvoiceNumber = inv?.InvoiceNumber;
        info.PrerequisiteInvoiceStatus = inv?.PaymentStatus ?? "Not Generated";
        info.PrerequisiteGrandTotal = inv?.GrandTotal;
        info.PrerequisitePaidAmount = inv?.EffectivePaidAmount;

        var isPaid = inv != null && (
            string.Equals(inv.PaymentStatus, "Paid", StringComparison.OrdinalIgnoreCase) ||
            (inv.GrandTotal > 0 && inv.EffectivePaidAmount >= inv.GrandTotal)
        );
        info.PrerequisitePaid = isPaid;

        if (!isCompleted)
        {
            info.IsLocked = true;
            info.LockReason = $"Locked: Milestone #{index} ({prev.MilestoneCode}: {prev.MilestoneName}) is not completed yet (Status: {prev.Status}). Complete Milestone #{index} to proceed.";
        }
        else if (!info.PrerequisiteInvoiced)
        {
            info.IsLocked = true;
            info.LockReason = $"Locked: Milestone #{index} ({prev.MilestoneCode}) is completed, but its Invoice has not been generated yet. Generate invoice in Finance module to proceed.";
        }
        else if (!isPaid)
        {
            info.IsLocked = true;
            info.LockReason = $"Locked: Milestone #{index} Invoice ({inv!.InvoiceNumber}) is currently {inv.PaymentStatus}. Full payment is required before Milestone #{seq} can start.";
        }
        else
        {
            info.IsLocked = false;
            info.LockReason = "";
        }

        return info;
    }

    /// <summary>
    /// Calculates the dependency and lock state for a task.
    /// Supports explicit dependency (specific prerequisite task), explicit no dependency, or sequential default.
    /// </summary>
    public static TaskDependencyInfo EvaluateTask(
        WorkTask task,
        IEnumerable<WorkTask> scopeTasks,
        WorkMilestone? parentMilestone,
        IEnumerable<WorkMilestone> allMilestonesInProject,
        IEnumerable<WorkInvoice> projectInvoices,
        IEnumerable<WorkTask>? allProjectTasks = null,
        bool? explicitHasDependency = null,
        int? explicitDependencyTaskId = null)
    {
        var info = new TaskDependencyInfo
        {
            DependencyType = "Strict Finish-to-Start Task Dependency"
        };

        // 1. Check parent milestone lock
        if (parentMilestone != null)
        {
            info.ParentMilestoneCode = parentMilestone.MilestoneCode;
            var msInfo = EvaluateMilestone(parentMilestone, allMilestonesInProject, projectInvoices);
            if (msInfo.IsLocked)
            {
                info.IsLocked = true;
                info.ParentMilestoneLocked = true;
                info.LockReason = $"Locked: Parent Milestone ({parentMilestone.MilestoneCode}: {parentMilestone.MilestoneName}) is locked by previous milestone invoice dependency.";
                return info;
            }
        }

        // 2. Check explicit dependency settings
        bool hasDep = false;
        int? depTaskId = null;

        if (explicitHasDependency.HasValue)
        {
            hasDep = explicitHasDependency.Value;
            depTaskId = explicitDependencyTaskId;
        }
        else
        {
            var parsed = ParseTaskDependencyNotes(task.Notes);
            hasDep = parsed.HasDependency;
            depTaskId = parsed.DependencyTaskId;
        }

        // Case A: Explicit NO Dependency
        if (!hasDep && (explicitHasDependency.HasValue || (task.Notes != null && task.Notes.Contains("[NO_DEP]"))))
        {
            info.DependencyType = "No Dependency (Independent Task)";
            info.IsLocked = false;
            info.LockReason = "";
            info.PrerequisiteTaskId = null;
            info.PrerequisiteTaskCode = "";
            info.PrerequisiteTaskName = "None (Independent Task)";
            info.PrerequisiteCompleted = true;
            info.HasExplicitDependency = false;
            return info;
        }

        // Case B: Explicit DEPENDENCY with a specific prerequisite task
        if (hasDep && depTaskId.HasValue && depTaskId.Value > 0)
        {
            info.HasExplicitDependency = true;
            info.DependencyType = "Prerequisite Task Dependency";

            var allTasks = allProjectTasks ?? scopeTasks;
            var prereqTask = allTasks.FirstOrDefault(t => t.Id == depTaskId.Value);
            if (prereqTask != null)
            {
                info.PrerequisiteTaskId = prereqTask.Id;
                info.PrerequisiteTaskCode = prereqTask.TaskCode;
                info.PrerequisiteTaskName = prereqTask.TaskName;
                info.PrerequisiteStatus = prereqTask.Status;

                var isCompleted = string.Equals(prereqTask.Status, "Completed", StringComparison.OrdinalIgnoreCase) ||
                                  (prereqTask.TaskAmount > 0 && prereqTask.CompletedAmount >= prereqTask.TaskAmount) ||
                                  prereqTask.ProgressPercent >= 100;
                info.PrerequisiteCompleted = isCompleted;

                if (!isCompleted)
                {
                    info.IsLocked = true;
                    info.LockReason = $"Locked: Prerequisite Task ({prereqTask.TaskCode}: {prereqTask.TaskName}) must be Completed first (Current: {prereqTask.Status}).";
                }
                else
                {
                    info.IsLocked = false;
                    info.LockReason = "";
                }
                return info;
            }
        }

        // Case C: Sequential Fallback (Default)
        var ordered = scopeTasks.OrderBy(t => t.Id).ToList();
        var index = ordered.FindIndex(t => t.Id == task.Id);
        if (index < 0) index = ordered.Count;

        var seq = index + 1;
        info.SequenceIndex = seq;
        info.TotalTasks = Math.Max(ordered.Count, seq);

        if (index == 0)
        {
            info.IsLocked = false;
            info.LockReason = "";
            info.PrerequisiteCompleted = true;
            return info;
        }

        var prevTask = ordered[index - 1];
        info.PrerequisiteTaskId = prevTask.Id;
        info.PrerequisiteTaskCode = prevTask.TaskCode;
        info.PrerequisiteTaskName = prevTask.TaskName;
        info.PrerequisiteStatus = prevTask.Status;

        var isPrevCompleted = string.Equals(prevTask.Status, "Completed", StringComparison.OrdinalIgnoreCase) ||
                              (prevTask.TaskAmount > 0 && prevTask.CompletedAmount >= prevTask.TaskAmount) ||
                              prevTask.ProgressPercent >= 100;
        info.PrerequisiteCompleted = isPrevCompleted;

        if (!isPrevCompleted)
        {
            info.IsLocked = true;
            info.LockReason = $"Locked: Previous Task #{index} ({prevTask.TaskCode}: {prevTask.TaskName}) must be Completed first (Current: {prevTask.Status}).";
        }
        else
        {
            info.IsLocked = false;
            info.LockReason = "";
        }

        return info;
    }
}
