using Microsoft.EntityFrameworkCore;

namespace CrmLeadManagement.Models;

/// <summary>
/// Same public API as before (Leads, GetById, AddLead, UpdateLead, DeleteLead, Next*Number),
/// so LeadController / LeadsApiController / FinanceController don't need any changes.
/// Internally this now reads/writes SQL Server through AppDbContext instead of an
/// in-memory list. See DbAccessor for how the DbContext is reached from here.
/// </summary>
public static class LeadStore
{
    /// <summary>All leads, with their call/follow-up/requirement/visit/finance history loaded.</summary>
    public static List<Lead> Leads => DbAccessor.Db.Leads.ToList();

    /// <summary>Returns a tracked entity — callers can mutate it (e.g. lead.CallHistory.Add(...))
    /// and the change is saved automatically at the end of the request.</summary>
    public static Lead? GetById(string id) => DbAccessor.Db.Leads.FirstOrDefault(l => l.LeadId == id);

    /// <summary>Generates the next sequential Lead ID, e.g. LD-1009, guaranteed unique.</summary>
    public static string NextId()
    {
        var existingIds = DbAccessor.Db.Leads
            .Select(l => l.LeadId)
            .ToList();

        var maxNum = existingIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.StartsWith("LD-", StringComparison.OrdinalIgnoreCase) ? id.Substring(3) : id)
            .Select(n => int.TryParse(n, out var v) ? v : 1000)
            .DefaultIfEmpty(1000)
            .Max();

        int nextNum = Math.Max(maxNum + 1, 1001);
        while (existingIds.Any(id => string.Equals(id, $"LD-{nextNum}", StringComparison.OrdinalIgnoreCase)))
        {
            nextNum++;
        }
        return $"LD-{nextNum}";
    }

    /// <summary>Generates the next sequential Estimation number, e.g. EST-2005.</summary>
    public static string NextEstimationNumber()
    {
        var numbers = DbAccessor.Db.Estimations
            .Select(e => e.Number)
            .ToList();

        var maxNum = numbers
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n!.StartsWith("EST-", StringComparison.OrdinalIgnoreCase) ? n.Substring(4) : n)
            .Select(n => int.TryParse(n, out var v) ? v : 2000)
            .DefaultIfEmpty(2000)
            .Max();

        int next = Math.Max(maxNum + 1, 2001);
        while (numbers.Any(n => string.Equals(n, $"EST-{next}", StringComparison.OrdinalIgnoreCase)))
        {
            next++;
        }
        return $"EST-{next}";
    }

    /// <summary>Generates the next sequential Quotation number, e.g. QUO-2005.</summary>
    public static string NextQuotationNumber()
    {
        var numbers = DbAccessor.Db.Quotations
            .Select(q => q.Number)
            .ToList();

        var maxNum = numbers
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n!.StartsWith("QUO-", StringComparison.OrdinalIgnoreCase) ? n.Substring(4) : n)
            .Select(n => int.TryParse(n, out var v) ? v : 2000)
            .DefaultIfEmpty(2000)
            .Max();

        int next = Math.Max(maxNum + 1, 2001);
        while (numbers.Any(n => string.Equals(n, $"QUO-{next}", StringComparison.OrdinalIgnoreCase)))
        {
            next++;
        }
        return $"QUO-{next}";
    }

    /// <summary>Generates the next sequential Proposal number, e.g. PRO-2005.</summary>
    public static string NextProposalNumber()
    {
        var numbers = DbAccessor.Db.Proposals
            .Select(p => p.Number)
            .ToList();

        var maxNum = numbers
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n!.StartsWith("PRO-", StringComparison.OrdinalIgnoreCase) ? n.Substring(4) : n)
            .Select(n => int.TryParse(n, out var v) ? v : 2000)
            .DefaultIfEmpty(2000)
            .Max();

        int next = Math.Max(maxNum + 1, 2001);
        while (numbers.Any(n => string.Equals(n, $"PRO-{next}", StringComparison.OrdinalIgnoreCase)))
        {
            next++;
        }
        return $"PRO-{next}";
    }

    /// <summary>Inserts a brand-new lead into the database. Shows up immediately in Lead List / Assigned List.</summary>
    public static Lead AddLead(Lead lead)
    {
        if (string.IsNullOrWhiteSpace(lead.LeadId) || DbAccessor.Db.Leads.Any(l => l.LeadId == lead.LeadId))
            lead.LeadId = NextId();
        if (string.IsNullOrWhiteSpace(lead.CreatedDate))
            lead.CreatedDate = DateTime.UtcNow.ToString("yyyy-MM-dd");
        if (string.IsNullOrWhiteSpace(lead.AssignedDate))
            lead.AssignedDate = DateTime.UtcNow.ToString("yyyy-MM-dd");

        if (!string.IsNullOrWhiteSpace(lead.AssignedTo) && lead.AssignmentHistory.Count == 0)
        {
            lead.AssignmentHistory.Add(new AssignmentEntry
            {
                AssignedEmployee = lead.AssignedTo,
                AssignmentDate = lead.AssignedDate,
                Remarks = "Initial lead assignment upon creation"
            });
        }

        DbAccessor.Db.Leads.Add(lead);
        DbAccessor.Db.SaveChanges();
        return lead;
    }

    /// <summary>
    /// Updates the core fields of an existing lead in place, preserving its call/follow-up/
    /// requirement/visit history so nothing already logged against the lead is lost.
    /// </summary>
    public static bool UpdateLead(Lead updated)
    {
        var existing = GetById(updated.LeadId);
        if (existing is null) return false;

        existing.LeadName = updated.LeadName;
        existing.CompanyName = updated.CompanyName;
        existing.MobileNumber = updated.MobileNumber;
        existing.Email = updated.Email;
        existing.Designation = updated.Designation;
        existing.Location = updated.Location;
        existing.LeadStatus = updated.LeadStatus;
        existing.Source = updated.Source;
        existing.AssignedTo = updated.AssignedTo;
        existing.FollowUpDate = updated.FollowUpDate;
        existing.Priority = updated.Priority;
        existing.Remarks = updated.Remarks;

        DbAccessor.Db.SaveChanges();
        return true;
    }

    /// <summary>Removes a lead permanently from the database.</summary>
    public static bool DeleteLead(string id)
    {
        var existing = GetById(id);
        if (existing is null) return false;

        DbAccessor.Db.Leads.Remove(existing);
        DbAccessor.Db.SaveChanges();
        return true;
    }

    /// <summary>Called once at startup (see Program.cs) to seed sample leads and finance records,
    /// but only if the tables are empty — so it never overwrites real data on subsequent runs.</summary>
    public static void SeedIfEmpty(AppDbContext db)
    {
        if (!db.Leads.Any())
        {
            db.Leads.AddRange(new List<Lead>
            {
                new Lead
                {
                    LeadId = "LD-1001",
                    LeadName = "Vikram Rao",
                    CompanyName = "Orion Steel Industries",
                    MobileNumber = "+91 98765 43210",
                    Email = "vikram.rao@orionsteel.com",
                    Designation = "Procurement Manager",
                    Location = "Pune, Maharashtra",
                    LeadStatus = "Qualified",
                    Source = "Website",
                    AssignedTo = "Ananya Sharma",
                    CreatedDate = "2026-08-02",
                    FollowUpDate = "2026-09-15",
                    Priority = "High",
                    AssignedDate = "2026-08-03",
                    Remarks = "Interested in bulk steel supply for new plant. Requested price quote.",
                    CallHistory = new()
                    {
                        new CallHistoryEntry{ CallDate="2026-08-04", CallTime="10:15 AM", CallType="Outgoing", Duration="6m 20s", Employee="Ananya Sharma", CallStatus="Connected", Notes="Discussed requirement scope, sending catalog." },
                        new CallHistoryEntry{ CallDate="2026-08-10", CallTime="03:40 PM", CallType="Incoming", Duration="3m 05s", Employee="Ananya Sharma", CallStatus="Connected", Notes="Client asked about delivery timelines." },
                    },
                    FollowUps = new()
                    {
                        new FollowUpEntry{ FollowUpDate="2026-08-12", FollowUpTime="11:00 AM", FollowUpType="Email", AssignedEmployee="Ananya Sharma", Status="Completed", Remarks="Sent detailed price quote via email." },
                        new FollowUpEntry{ FollowUpDate="2026-09-15", FollowUpTime="02:00 PM", FollowUpType="Call", AssignedEmployee="Ananya Sharma", Status="Pending", Remarks="Confirm decision on quote." },
                    },
                    Requirements = new()
                    {
                        new RequirementEntry{ RequirementType="Price Quote", ItemName="MS Steel Rods (12mm)", Quantity="500 tons", Priority="High", Details="Quote needed for Q4 plant expansion.", ExpectedDate="2026-08-15", Remarks="Urgent, competing with two other vendors." }
                    },
                    Visits = new()
                    {
                        new VisitEntry{ VisitDate="2026-08-06", VisitTime="11:30 AM", VisitLocation="Orion Steel Industries, MIDC Bhosari, Pune", Employee="Ananya Sharma", Conversation="Walked through the plant expansion plan and shared sample catalog.", Outcome="Interested", Remarks="Client wants a formal price quote within a week." },
                        new VisitEntry{ VisitDate="2026-09-02", VisitTime="04:00 PM", VisitLocation="Orion Steel Industries, MIDC Bhosari, Pune", Employee="Ananya Sharma", Conversation="Reviewed the quote in person, discussed payment terms.", Outcome="Follow-up Needed", Remarks="Awaiting internal budget approval on their end." },
                    },
                    Estimations = new()
                    {
                        new EstimationEntry
                        {
                            Number = "EST-2001",
                            Date = "2026-08-15",
                            LeadCustomer = "Vikram Rao (Orion Steel Industries)",
                            ProjectRequirement = "Bulk MS Steel Rods (12mm) Supply for Q4 Expansion",
                            Items = new()
                            {
                                new FinanceLineItem { ItemName = "MS Steel Rods 12mm (Fe550D)", Quantity = "500", Rate = "55000", Amount = "27500000" },
                                new FinanceLineItem { ItemName = "Freight & Safe Delivery to MIDC Bhosari", Quantity = "1", Rate = "125000", Amount = "125000" }
                            },
                            Subtotal = "27625000",
                            Discount = "25000",
                            Tax = "4968000",
                            Total = "32568000",
                            ValidUntil = "2026-09-30",
                            TermsConditions = "30% Advance with PO, 70% against dispatch confirmation.",
                            Notes = "Delivery within 10-14 working days post order confirmation.",
                            Status = "Approved"
                        }
                    },
                    Quotations = new()
                    {
                        new QuotationEntry
                        {
                            Number = "QUO-2001",
                            Date = "2026-08-20",
                            LeadCustomer = "Vikram Rao (Orion Steel Industries)",
                            ProjectRequirement = "MS Steel Rods - Formal Plant Quotation",
                            Items = new()
                            {
                                new FinanceLineItem { ItemName = "MS Steel Rods Grade Fe550D (12mm)", Quantity = "500", Rate = "54500", Amount = "27250000" },
                                new FinanceLineItem { ItemName = "Quality Inspection & Mill Test Certificate", Quantity = "1", Rate = "50000", Amount = "50000" }
                            },
                            Subtotal = "27300000",
                            Discount = "50000",
                            Tax = "4905000",
                            GrandTotal = "32155000",
                            ValidUntil = "2026-09-25",
                            TermsConditions = "Payment strictly as per standard credit terms approved by finance.",
                            Notes = "Mill test certificates will be provided with each batch.",
                            Status = "Sent"
                        }
                    },
                    Proposals = new()
                    {
                        new ProposalEntry
                        {
                            Number = "PRO-2001",
                            Date = "2026-08-25",
                            LeadCustomer = "Vikram Rao (Orion Steel Industries)",
                            ProjectRequirement = "Plant Expansion Steel Procurement Package",
                            Title = "Comprehensive Annual Steel Supply & Logistics Framework",
                            ScopeOfWork = "Supply of 500 tons per quarter of high-grade MS Steel Rods with scheduled logistics to MIDC Bhosari",
                            Description = "End-to-end material supply agreement ensuring uninterrupted procurement pipeline with guaranteed price locks.",
                            Deliverables = "Quarterly scheduled deliveries, ISO certified mill test reports, dedicated account manager.",
                            Timeline = "12 Months",
                            Budget = "32000000",
                            TermsConditions = "Contract renewal subject to annual review.",
                            Notes = "Awaiting procurement director review.",
                            Status = "Sent"
                        }
                    }
                },
                new Lead
                {
                    LeadId = "LD-1002",
                    LeadName = "Meera Iyer",
                    CompanyName = "Bluewave Textiles",
                    MobileNumber = "+91 91234 56780",
                    Email = "meera.iyer@bluewavetex.com",
                    Designation = "CEO",
                    Location = "Coimbatore, Tamil Nadu",
                    LeadStatus = "New",
                    Source = "Referral",
                    AssignedTo = "Rohit Verma",
                    CreatedDate = "2026-09-01",
                    FollowUpDate = "2026-09-14",
                    Priority = "Medium",
                    AssignedDate = "2026-09-01",
                    Remarks = "Referred by an existing client. Yet to be contacted.",
                },
                new Lead
                {
                    LeadId = "LD-1003",
                    LeadName = "Sanjay Kapoor",
                    CompanyName = "Nexgen Electronics",
                    MobileNumber = "+91 90000 11223",
                    Email = "sanjay.k@nexgenelec.in",
                    Designation = "Operations Head",
                    Location = "Bengaluru, Karnataka",
                    LeadStatus = "Proposal Sent",
                    Source = "Trade Show",
                    AssignedTo = "Priya Nair",
                    CreatedDate = "2026-07-20",
                    FollowUpDate = "2026-09-13",
                    Priority = "High",
                    AssignedDate = "2026-07-21",
                    Remarks = "Met at Bengaluru tech expo, strong interest in demo units.",
                    CallHistory = new()
                    {
                        new CallHistoryEntry{ CallDate="2026-07-25", CallTime="12:00 PM", CallType="Outgoing", Duration="8m 40s", Employee="Priya Nair", CallStatus="Connected", Notes="Walked through product lineup." }
                    },
                    Requirements = new()
                    {
                        new RequirementEntry{ RequirementType="Demo", ItemName="NX-200 Controller Unit", Quantity="2 units", Priority="Medium", Details="On-site demo requested for engineering team.", ExpectedDate="2026-09-20", Remarks="Prefers demo on a Friday." }
                    },
                    Visits = new()
                    {
                        new VisitEntry{ VisitDate="2026-07-28", VisitTime="02:15 PM", VisitLocation="Nexgen Electronics, Whitefield, Bengaluru", Employee="Priya Nair", Conversation="Introduced the product lineup and answered technical questions.", Outcome="Interested", Remarks="Engineering team to review specs before demo." }
                    },
                    Quotations = new()
                    {
                        new QuotationEntry
                        {
                            Number = "QUO-2002",
                            Date = "2026-08-01",
                            LeadCustomer = "Sanjay Kapoor (Nexgen Electronics)",
                            ProjectRequirement = "Controller Units Demo & Trial Batch",
                            Items = new()
                            {
                                new FinanceLineItem { ItemName = "NX-200 Controller Unit (Industrial Grade)", Quantity = "2", Rate = "45000", Amount = "90000" },
                                new FinanceLineItem { ItemName = "On-site Calibration & Integration Support", Quantity = "1", Rate = "15000", Amount = "15000" }
                            },
                            Subtotal = "105000",
                            Discount = "5000",
                            Tax = "18000",
                            GrandTotal = "118000",
                            ValidUntil = "2026-09-15",
                            TermsConditions = "100% on delivery of trial units.",
                            Notes = "Trial units covered under 1 year replacement warranty.",
                            Status = "Approved"
                        }
                    },
                    Proposals = new()
                    {
                        new ProposalEntry
                        {
                            Number = "PRO-2002",
                            Date = "2026-08-10",
                            LeadCustomer = "Sanjay Kapoor (Nexgen Electronics)",
                            ProjectRequirement = "Smart Manufacturing Line Controllers",
                            Title = "NX-200 Automation Controller Integration for Whitefield Plant",
                            ScopeOfWork = "Complete rollout of 50 NX-200 controller modules with firmware customisation and SCADA integration",
                            Description = "Modernizing electronics testing lines with high-throughput microcontrollers.",
                            Deliverables = "50 Controller units, API integration suite, 24/7 technical hotline.",
                            Timeline = "3 Months",
                            Budget = "2450000",
                            TermsConditions = "Milestone based payments upon deployment approval.",
                            Notes = "Implementation phase starting October.",
                            Status = "Approved"
                        }
                    }
                },
                new Lead
                {
                    LeadId = "LD-1004",
                    LeadName = "Farhan Sheikh",
                    CompanyName = "Coastal Logistics Pvt Ltd",
                    MobileNumber = "+91 99887 76655",
                    Email = "farhan.sheikh@coastallog.com",
                    Designation = "Fleet Manager",
                    Location = "Mumbai, Maharashtra",
                    LeadStatus = "Negotiation",
                    Source = "Cold Call",
                    AssignedTo = "Karan Mehta",
                    CreatedDate = "2026-06-11",
                    FollowUpDate = "2026-09-16",
                    Priority = "High",
                    AssignedDate = "2026-06-12",
                    Remarks = "Negotiating annual maintenance contract terms.",
                    Visits = new()
                    {
                        new VisitEntry{ VisitDate="2026-09-05", VisitTime="10:00 AM", VisitLocation="Coastal Logistics Pvt Ltd, JNPT Road, Mumbai", Employee="Karan Mehta", Conversation="Discussed AMC pricing tiers and SLA terms with the fleet team.", Outcome="Follow-up Needed", Remarks="Waiting on their legal team to review the contract draft." }
                    },
                    Estimations = new()
                    {
                        new EstimationEntry
                        {
                            Number = "EST-2002",
                            Date = "2026-07-15",
                            LeadCustomer = "Farhan Sheikh (Coastal Logistics Pvt Ltd)",
                            ProjectRequirement = "Annual Fleet Maintenance Contract (AMC)",
                            Items = new()
                            {
                                new FinanceLineItem { ItemName = "Heavy Commercial Vehicle Preventive AMC (50 Trucks)", Quantity = "50", Rate = "24000", Amount = "1200000" },
                                new FinanceLineItem { ItemName = "Emergency 24x7 Highway On-Road Assistance Coverage", Quantity = "1", Rate = "180000", Amount = "180000" }
                            },
                            Subtotal = "1380000",
                            Discount = "30000",
                            Tax = "243000",
                            Total = "1593000",
                            ValidUntil = "2026-09-30",
                            TermsConditions = "Quarterly invoicing in advance.",
                            Notes = "Includes free spare part labor inspection.",
                            Status = "Sent"
                        }
                    },
                    Proposals = new()
                    {
                        new ProposalEntry
                        {
                            Number = "PRO-2003",
                            Date = "2026-07-22",
                            LeadCustomer = "Farhan Sheikh (Coastal Logistics Pvt Ltd)",
                            ProjectRequirement = "Fleet AMC & GPS Telematics SLA",
                            Title = "Coastal Logistics Complete Fleet Support & Telematics SLA",
                            ScopeOfWork = "Fleet wide maintenance and real-time engine telemetry monitoring for 50 commercial vehicles",
                            Description = "24/7 SLA guaranteeing 98% fleet uptime with automated diagnostic reporting and priority spare parts dispatch.",
                            Deliverables = "Fleet maintenance plan, monthly audit reports, spare parts discount catalogue.",
                            Timeline = "12 Months",
                            Budget = "1600000",
                            TermsConditions = "Annual SLA with 30 days termination clause.",
                            Notes = "Final contract draft under legal review.",
                            Status = "Draft"
                        }
                    }
                },
                new Lead
                {
                    LeadId = "LD-1005",
                    LeadName = "Divya Menon",
                    CompanyName = "Sunrise Pharma",
                    MobileNumber = "+91 98111 22334",
                    Email = "divya.menon@sunrisepharma.com",
                    Designation = "Supply Chain Lead",
                    Location = "Hyderabad, Telangana",
                    LeadStatus = "Contacted",
                    Source = "Social Media",
                    AssignedTo = "Sneha Kulkarni",
                    CreatedDate = "2026-08-25",
                    FollowUpDate = "2026-09-18",
                    Priority = "Low",
                    AssignedDate = "2026-08-26",
                    Remarks = "Early stage conversation, gauging interest.",
                },
                new Lead
                {
                    LeadId = "LD-1006",
                    LeadName = "Aditya Joshi",
                    CompanyName = "Greenfield Agro",
                    MobileNumber = "+91 97766 55443",
                    Email = "aditya.joshi@greenfieldagro.in",
                    Designation = "Director",
                    Location = "Nagpur, Maharashtra",
                    LeadStatus = "Won",
                    Source = "Partner",
                    AssignedTo = "Arjun Desai",
                    CreatedDate = "2026-05-15",
                    FollowUpDate = "2026-09-10",
                    Priority = "Medium",
                    AssignedDate = "2026-05-16",
                    Remarks = "Deal closed, onboarding in progress.",
                },
                new Lead
                {
                    LeadId = "LD-1007",
                    LeadName = "Neha Kulkarni",
                    CompanyName = "Skyline Constructions",
                    MobileNumber = "+91 96655 44332",
                    Email = "neha.k@skylineconstruct.com",
                    Designation = "Project Manager",
                    Location = "Nashik, Maharashtra",
                    LeadStatus = "Lost",
                    Source = "Advertisement",
                    AssignedTo = "Ananya Sharma",
                    CreatedDate = "2026-04-02",
                    FollowUpDate = "2026-06-05",
                    Priority = "Low",
                    AssignedDate = "2026-04-03",
                    Remarks = "Went with a competitor due to pricing.",
                },
                new Lead
                {
                    LeadId = "LD-1008",
                    LeadName = "Ramesh Chandran",
                    CompanyName = "Pinnacle Realty",
                    MobileNumber = "+91 95544 33221",
                    Email = "ramesh.c@pinnaclerealty.in",
                    Designation = "VP Sales",
                    Location = "Chennai, Tamil Nadu",
                    LeadStatus = "Qualified",
                    Source = "Website",
                    AssignedTo = "Rohit Verma",
                    CreatedDate = "2026-08-18",
                    FollowUpDate = "2026-09-14",
                    Priority = "Medium",
                    AssignedDate = "2026-08-19",
                    Remarks = "Wants a tailored proposal for 3 upcoming sites.",
                    Estimations = new()
                    {
                        new EstimationEntry
                        {
                            Number = "EST-2003",
                            Date = "2026-08-22",
                            LeadCustomer = "Ramesh Chandran (Pinnacle Realty)",
                            ProjectRequirement = "Commercial Property CRM & Site Pipeline Setup",
                            Items = new()
                            {
                                new FinanceLineItem { ItemName = "Multi-site CRM License & Cloud Setup", Quantity = "3", Rate = "120000", Amount = "360000" },
                                new FinanceLineItem { ItemName = "Data Migration & Custom Sales Funnel", Quantity = "1", Rate = "85000", Amount = "85000" }
                            },
                            Subtotal = "445000",
                            Discount = "15000",
                            Tax = "77400",
                            Total = "507400",
                            ValidUntil = "2026-10-15",
                            TermsConditions = "50% advance upon setup initiation.",
                            Notes = "Estimated go-live within 3 weeks.",
                            Status = "Draft"
                        }
                    },
                    Quotations = new()
                    {
                        new QuotationEntry
                        {
                            Number = "QUO-2003",
                            Date = "2026-08-28",
                            LeadCustomer = "Ramesh Chandran (Pinnacle Realty)",
                            ProjectRequirement = "CRM Deployment for 3 Sites",
                            Items = new()
                            {
                                new FinanceLineItem { ItemName = "CRM Enterprise 3-Site Package", Quantity = "1", Rate = "420000", Amount = "420000" },
                                new FinanceLineItem { ItemName = "User Onboarding & Staff Training (20 seats)", Quantity = "20", Rate = "3500", Amount = "70000" }
                            },
                            Subtotal = "490000",
                            Discount = "20000",
                            Tax = "84600",
                            GrandTotal = "554600",
                            ValidUntil = "2026-10-01",
                            TermsConditions = "Includes 3 months post-deployment maintenance.",
                            Notes = "Ready for sign-off.",
                            Status = "Draft"
                        }
                    }
                },
            });
            db.SaveChanges();
        }
        else
        {
            // Backfill finance data if existing database has leads without estimations/quotations/proposals
            var lead1 = db.Leads.FirstOrDefault(l => l.LeadId == "LD-1001");
            if (lead1 is not null && lead1.Estimations.Count == 0 && lead1.Quotations.Count == 0 && lead1.Proposals.Count == 0)
            {
                lead1.Estimations.Add(new EstimationEntry
                {
                    Number = "EST-2001",
                    Date = "2026-08-15",
                    LeadCustomer = "Vikram Rao (Orion Steel Industries)",
                    ProjectRequirement = "Bulk MS Steel Rods (12mm) Supply for Q4 Expansion",
                    Items = new()
                    {
                        new FinanceLineItem { ItemName = "MS Steel Rods 12mm (Fe550D)", Quantity = "500", Rate = "55000", Amount = "27500000" },
                        new FinanceLineItem { ItemName = "Freight & Safe Delivery to MIDC Bhosari", Quantity = "1", Rate = "125000", Amount = "125000" }
                    },
                    Subtotal = "27625000",
                    Discount = "25000",
                    Tax = "4968000",
                    Total = "32568000",
                    ValidUntil = "2026-09-30",
                    TermsConditions = "30% Advance with PO, 70% against dispatch confirmation.",
                    Notes = "Delivery within 10-14 working days post order confirmation.",
                    Status = "Approved"
                });

                lead1.Quotations.Add(new QuotationEntry
                {
                    Number = "QUO-2001",
                    Date = "2026-08-20",
                    LeadCustomer = "Vikram Rao (Orion Steel Industries)",
                    ProjectRequirement = "MS Steel Rods - Formal Plant Quotation",
                    Items = new()
                    {
                        new FinanceLineItem { ItemName = "MS Steel Rods Grade Fe550D (12mm)", Quantity = "500", Rate = "54500", Amount = "27250000" },
                        new FinanceLineItem { ItemName = "Quality Inspection & Mill Test Certificate", Quantity = "1", Rate = "50000", Amount = "50000" }
                    },
                    Subtotal = "27300000",
                    Discount = "50000",
                    Tax = "4905000",
                    GrandTotal = "32155000",
                    ValidUntil = "2026-09-25",
                    TermsConditions = "Payment strictly as per standard credit terms approved by finance.",
                    Notes = "Mill test certificates will be provided with each batch.",
                    Status = "Sent"
                });

                lead1.Proposals.Add(new ProposalEntry
                {
                    Number = "PRO-2001",
                    Date = "2026-08-25",
                    LeadCustomer = "Vikram Rao (Orion Steel Industries)",
                    ProjectRequirement = "Plant Expansion Steel Procurement Package",
                    Title = "Comprehensive Annual Steel Supply & Logistics Framework",
                    ScopeOfWork = "Supply of 500 tons per quarter of high-grade MS Steel Rods with scheduled logistics to MIDC Bhosari",
                    Description = "End-to-end material supply agreement ensuring uninterrupted procurement pipeline with guaranteed price locks.",
                    Deliverables = "Quarterly scheduled deliveries, ISO certified mill test reports, dedicated account manager.",
                    Timeline = "12 Months",
                    Budget = "32000000",
                    TermsConditions = "Contract renewal subject to annual review.",
                    Notes = "Awaiting procurement director review.",
                    Status = "Sent"
                });
            }

            var lead3 = db.Leads.FirstOrDefault(l => l.LeadId == "LD-1003");
            if (lead3 is not null && lead3.Quotations.Count == 0 && lead3.Proposals.Count == 0)
            {
                lead3.Quotations.Add(new QuotationEntry
                {
                    Number = "QUO-2002",
                    Date = "2026-08-01",
                    LeadCustomer = "Sanjay Kapoor (Nexgen Electronics)",
                    ProjectRequirement = "Controller Units Demo & Trial Batch",
                    Items = new()
                    {
                        new FinanceLineItem { ItemName = "NX-200 Controller Unit (Industrial Grade)", Quantity = "2", Rate = "45000", Amount = "90000" },
                        new FinanceLineItem { ItemName = "On-site Calibration & Integration Support", Quantity = "1", Rate = "15000", Amount = "15000" }
                    },
                    Subtotal = "105000",
                    Discount = "5000",
                    Tax = "18000",
                    GrandTotal = "118000",
                    ValidUntil = "2026-09-15",
                    TermsConditions = "100% on delivery of trial units.",
                    Notes = "Trial units covered under 1 year replacement warranty.",
                    Status = "Approved"
                });

                lead3.Proposals.Add(new ProposalEntry
                {
                    Number = "PRO-2002",
                    Date = "2026-08-10",
                    LeadCustomer = "Sanjay Kapoor (Nexgen Electronics)",
                    ProjectRequirement = "Smart Manufacturing Line Controllers",
                    Title = "NX-200 Automation Controller Integration for Whitefield Plant",
                    ScopeOfWork = "Complete rollout of 50 NX-200 controller modules with firmware customisation and SCADA integration",
                    Description = "Modernizing electronics testing lines with high-throughput microcontrollers.",
                    Deliverables = "50 Controller units, API integration suite, 24/7 technical hotline.",
                    Timeline = "3 Months",
                    Budget = "2450000",
                    TermsConditions = "Milestone based payments upon deployment approval.",
                    Notes = "Implementation phase starting October.",
                    Status = "Approved"
                });
            }

            var lead4 = db.Leads.FirstOrDefault(l => l.LeadId == "LD-1004");
            if (lead4 is not null && lead4.Estimations.Count == 0 && lead4.Proposals.Count == 0)
            {
                lead4.Estimations.Add(new EstimationEntry
                {
                    Number = "EST-2002",
                    Date = "2026-07-15",
                    LeadCustomer = "Farhan Sheikh (Coastal Logistics Pvt Ltd)",
                    ProjectRequirement = "Annual Fleet Maintenance Contract (AMC)",
                    Items = new()
                    {
                        new FinanceLineItem { ItemName = "Heavy Commercial Vehicle Preventive AMC (50 Trucks)", Quantity = "50", Rate = "24000", Amount = "1200000" },
                        new FinanceLineItem { ItemName = "Emergency 24x7 Highway On-Road Assistance Coverage", Quantity = "1", Rate = "180000", Amount = "180000" }
                    },
                    Subtotal = "1380000",
                    Discount = "30000",
                    Tax = "243000",
                    Total = "1593000",
                    ValidUntil = "2026-09-30",
                    TermsConditions = "Quarterly invoicing in advance.",
                    Notes = "Includes free spare part labor inspection.",
                    Status = "Sent"
                });

                lead4.Proposals.Add(new ProposalEntry
                {
                    Number = "PRO-2003",
                    Date = "2026-07-22",
                    LeadCustomer = "Farhan Sheikh (Coastal Logistics Pvt Ltd)",
                    ProjectRequirement = "Fleet AMC & GPS Telematics SLA",
                    Title = "Coastal Logistics Complete Fleet Support & Telematics SLA",
                    ScopeOfWork = "Fleet wide maintenance and real-time engine telemetry monitoring for 50 commercial vehicles",
                    Description = "24/7 SLA guaranteeing 98% fleet uptime with automated diagnostic reporting and priority spare parts dispatch.",
                    Deliverables = "Fleet maintenance plan, monthly audit reports, spare parts discount catalogue.",
                    Timeline = "12 Months",
                    Budget = "1600000",
                    TermsConditions = "Annual SLA with 30 days termination clause.",
                    Notes = "Final contract draft under legal review.",
                    Status = "Draft"
                });
            }

            var lead8 = db.Leads.FirstOrDefault(l => l.LeadId == "LD-1008");
            if (lead8 is not null && lead8.Estimations.Count == 0 && lead8.Quotations.Count == 0)
            {
                lead8.Estimations.Add(new EstimationEntry
                {
                    Number = "EST-2003",
                    Date = "2026-08-22",
                    LeadCustomer = "Ramesh Chandran (Pinnacle Realty)",
                    ProjectRequirement = "Commercial Property CRM & Site Pipeline Setup",
                    Items = new()
                    {
                        new FinanceLineItem { ItemName = "Multi-site CRM License & Cloud Setup", Quantity = "3", Rate = "120000", Amount = "360000" },
                        new FinanceLineItem { ItemName = "Data Migration & Custom Sales Funnel", Quantity = "1", Rate = "85000", Amount = "85000" }
                    },
                    Subtotal = "445000",
                    Discount = "15000",
                    Tax = "77400",
                    Total = "507400",
                    ValidUntil = "2026-10-15",
                    TermsConditions = "50% advance upon setup initiation.",
                    Notes = "Estimated go-live within 3 weeks.",
                    Status = "Draft"
                });

                lead8.Quotations.Add(new QuotationEntry
                {
                    Number = "QUO-2003",
                    Date = "2026-08-28",
                    LeadCustomer = "Ramesh Chandran (Pinnacle Realty)",
                    ProjectRequirement = "CRM Deployment for 3 Sites",
                    Items = new()
                    {
                        new FinanceLineItem { ItemName = "CRM Enterprise 3-Site Package", Quantity = "1", Rate = "420000", Amount = "420000" },
                        new FinanceLineItem { ItemName = "User Onboarding & Staff Training (20 seats)", Quantity = "20", Rate = "3500", Amount = "70000" }
                    },
                    Subtotal = "490000",
                    Discount = "20000",
                    Tax = "84600",
                    GrandTotal = "554600",
                    ValidUntil = "2026-10-01",
                    TermsConditions = "Includes 3 months post-deployment maintenance.",
                    Notes = "Ready for sign-off.",
                    Status = "Draft"
                });
            }

            db.SaveChanges();
        }
    }
}
