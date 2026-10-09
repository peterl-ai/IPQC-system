using ClosedXML.Excel;
using JaxPower.Ipqc.Api.Contracts;

namespace JaxPower.Ipqc.Api.Services;

public sealed class CompletedPatrolReportExcel
{
    private static readonly string[] ItemHeaders = ["No.", "Process", "Inspection Item", "Inspection Content",
        "Type", "Lower Limit", "Upper Limit", "Sampling Plan", "Required Samples", "N/A",
        "Item Judgment", "Item Inspection Time", "Sample No.", "Inspection Value", "Sample Judgment",
        "Sample Inspection Time", "Machine Code", "Series", "Mold", "Abnormal Type",
        "Abnormal Cause", "Remarks", "Photo Requirement", "Defect Level"];
    private static readonly string[] RevisionHeaders = ["Revision No.", "Shift", "Submitted By", "Submitted At",
        "Overall Result", "Review Decision", "Reviewed By", "Reviewed At", "Reject Reason",
        "Item No.", "Item Judgment", "Item Inspection Time", "Sample No.", "Sample Value", "Sample Judgment"];

    public byte[] Write(CompletedPatrolReport report)
    {
        using var book = new XLWorkbook();
        var sheet = book.AddWorksheet("Inspection Report");
        sheet.Cell(1, 1).Value = "JAX Power IPQC Inspection Report";
        sheet.Cell(1, 1).Style.Font.Bold = true;
        sheet.Cell(1, 1).Style.Font.FontSize = 16;
        var summary = report.Summary;
        var fields = new (string Label, string? Value)[] {
            ("Task No.", summary.TaskNo), ("Plan No.", summary.PlanNo), ("Plan Name", summary.PlanName),
            ("Patrol Standard Name", summary.StandardName), ("Factory", summary.FactoryName),
            ("Workshop", summary.WorkshopCode), ("Production Line", summary.LineName),
            ("Material Code", summary.MaterialCode), ("Inspector", summary.Inspector),
            ("Shift", summary.Shift), ("Scheduled Time", PlantTime(summary.ScheduledOccurrenceUtc)),
            ("Submitted Time", PlantTime(report.FinalSubmittedAtUtc)),
            ("Completed Time", PlantTime(summary.CompletedAtUtc)),
            ("Final Revision", report.FinalRevisionNo.ToString()),
            ("Overall Inspection Result", summary.OverallInspectionResult),
            ("Approved By", summary.ApprovedBy), ("Approved At", PlantTime(summary.ApprovedAtUtc))
        };
        for (var i = 0; i < fields.Length; i++)
        {
            Text(sheet.Cell(i + 3, 1), fields[i].Label);
            sheet.Cell(i + 3, 1).Style.Font.Bold = true;
            Text(sheet.Cell(i + 3, 2), fields[i].Value);
        }
        const int headerRow = 22;
        for (var i = 0; i < ItemHeaders.Length; i++) Header(sheet.Cell(headerRow, i + 1), ItemHeaders[i]);
        var row = headerRow + 1;
        foreach (var item in report.Items)
        {
            var samples = item.Samples.Count == 0 ? new CompletedRecordSample?[] { null } : item.Samples.Cast<CompletedRecordSample?>();
            foreach (var sample in samples)
            {
                var values = new[] { item.SequenceNo.ToString(), Join(item.ProcessCode, item.ProcessName), item.InspectionItem,
                    item.InspectionContent, item.InspectionType, Limit(item.LowerLimitOperator, item.LowerLimitValue),
                    Limit(item.UpperLimitOperator, item.UpperLimitValue), item.SamplingPlan, item.SampleCount,
                    item.IsNa ? "Yes" : "No", item.JudgmentResult, PlantTime(item.InspectedAtUtc),
                    sample?.SequenceNo.ToString(), sample?.InspectionValue?.ToString(), sample?.JudgmentResult,
                    PlantTime(sample?.InspectedAtUtc), item.MachineCode, item.Series, item.Mold,
                    item.AbnormalType, item.AbnormalCause, item.Remarks, item.PhotoRequirement, item.DefectLevel };
                for (var col = 0; col < values.Length; col++) Text(sheet.Cell(row, col + 1), values[col]);
                row++;
            }
        }
        sheet.SheetView.FreezeRows(headerRow);
        sheet.Columns(1, ItemHeaders.Length).Width = 20;
        sheet.Column(4).Width = 40;
        sheet.Column(22).Width = 40;
        sheet.Range(headerRow, 1, Math.Max(row - 1, headerRow), ItemHeaders.Length).Style.Alignment.WrapText = true;

        var history = book.AddWorksheet("Revision History");
        history.Cell(1, 1).Value = "Submission and Review History";
        history.Cell(1, 1).Style.Font.Bold = true;
        for (var i = 0; i < RevisionHeaders.Length; i++) Header(history.Cell(3, i + 1), RevisionHeaders[i]);
        row = 4;
        foreach (var revision in report.RevisionHistory)
            foreach (var item in revision.Items)
            {
                var samples = item.Samples.Count == 0 ? new CompletedRecordSample?[] { null } : item.Samples.Cast<CompletedRecordSample?>();
                foreach (var sample in samples)
                {
                    var values = new[] { revision.RevisionNo.ToString(), revision.Shift, revision.SubmittedBy,
                        PlantTime(revision.SubmittedAtUtc), revision.OverallInspectionResult, revision.ReviewDecision,
                        revision.ReviewedBy, PlantTime(revision.ReviewedAtUtc), revision.RejectReason,
                        item.SequenceNo.ToString(), item.JudgmentResult, PlantTime(item.InspectedAtUtc),
                        sample?.SequenceNo.ToString(), sample?.InspectionValue?.ToString(), sample?.JudgmentResult };
                    for (var col = 0; col < values.Length; col++) Text(history.Cell(row, col + 1), values[col]);
                    row++;
                }
            }
        history.SheetView.FreezeRows(3);
        history.Columns(1, RevisionHeaders.Length).Width = 22;
        history.Column(9).Width = 40;
        using var stream = new MemoryStream();
        book.SaveAs(stream);
        return stream.ToArray();
    }

    public static string PlantTime(DateTime? utc) => utc is { } time
        ? TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(time, DateTimeKind.Utc),
            CompletedPatrolRecordService.PlantZone).ToString("yyyy-MM-dd HH:mm:ss") : "";
    private static string Join(string left, string right) => string.Join(" / ", new[] { left, right }.Where(x => !string.IsNullOrWhiteSpace(x)));
    private static string Limit(string op, string value) => string.Join(" ", new[] { op, value }.Where(x => !string.IsNullOrWhiteSpace(x)));
    private static void Text(IXLCell cell, string? value)
    {
        // ClosedXML's string value API stores business text as a string cell, including leading formula characters.
        cell.SetValue(value ?? "");
        cell.Style.NumberFormat.Format = "@";
    }
    private static void Header(IXLCell cell, string label)
    {
        Text(cell, label);
        cell.Style.Font.Bold = true;
        cell.Style.Fill.BackgroundColor = XLColor.LightBlue;
    }
}
