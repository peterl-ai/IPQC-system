using ClosedXML.Excel;
using JaxPower.Ipqc.Api.Contracts;

namespace JaxPower.Ipqc.Api.Services;

public sealed class PatrolStandardExcel
{
    private static readonly string[] HeaderLabels = ["Factory Code", "Factory Name", "Workshop Code", "Line Code", "Line Name", "Patrol Standard Name", "Material Code"];
    private static readonly string[] ItemLabels = ["Process Code", "Process Name", "Inspection Item Category", "Inspection Item", "Inspection Content", "Upper Limit Operator", "Upper Limit Value", "Lower Limit Operator", "Lower Limit Value", "Inspection Type", "Sampling Plan", "Sample Count", "Photo Requirement", "Defect Level"];
    private const int ItemHeaderRow = 9;
    private const int FirstItemRow = 10;
    private const int MaxItems = 1000;

    public byte[] Write(StandardOutput? standard = null)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Patrol Standard");
        var values = standard is null ? [] : new[] { standard.FactoryCode, standard.FactoryName,
            standard.WorkshopCode, standard.LineCode, standard.LineName, standard.PatrolStandardName, standard.MaterialCode };
        for (var i = 0; i < HeaderLabels.Length; i++)
        {
            sheet.Cell(i + 1, 1).Value = HeaderLabels[i];
            sheet.Cell(i + 1, 1).Style.Font.Bold = true;
            sheet.Cell(i + 1, 2).Value = values.Length == 0 ? "" : values[i];
            sheet.Cell(i + 1, 2).Style.NumberFormat.Format = "@";
        }
        for (var i = 0; i < ItemLabels.Length; i++)
        {
            sheet.Cell(ItemHeaderRow, i + 1).Value = ItemLabels[i];
            sheet.Cell(ItemHeaderRow, i + 1).Style.Font.Bold = true;
            sheet.Column(i + 1).Width = i == 4 ? 34 : 21;
            sheet.Column(i + 1).Style.NumberFormat.Format = "@";
        }
        if (standard is not null)
        {
            foreach (var (item, index) in standard.InspectionItems.Select((x, i) => (x, i)))
            {
                var fields = new[] { item.ProcessCode, item.ProcessName, item.InspectionItemCategory,
                    item.InspectionItem, item.InspectionContent, item.UpperLimitOperator, item.UpperLimitValue,
                    item.LowerLimitOperator, item.LowerLimitValue, item.InspectionType, item.SamplingPlan,
                    item.SampleCount, item.PhotoRequirement, item.DefectLevel };
                for (var col = 0; col < fields.Length; col++) sheet.Cell(FirstItemRow + index, col + 1).Value = fields[col];
            }
        }
        sheet.SheetView.FreezeRows(ItemHeaderRow);
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    public StandardInput Read(Stream stream)
    {
        try
        {
            using var workbook = new XLWorkbook(stream);
            if (workbook.Worksheets.Count != 1 || workbook.Worksheet(1).Name != "Patrol Standard")
                throw new ExcelValidationException("Workbook must contain exactly one 'Patrol Standard' sheet.");
            var sheet = workbook.Worksheet(1);
            for (var i = 0; i < HeaderLabels.Length; i++)
                if (sheet.Cell(i + 1, 1).GetString().Trim() != HeaderLabels[i])
                    throw new ExcelValidationException($"Invalid header label at row {i + 1}.");
            for (var i = 0; i < ItemLabels.Length; i++)
                if (sheet.Cell(ItemHeaderRow, i + 1).GetString().Trim() != ItemLabels[i])
                    throw new ExcelValidationException($"Invalid inspection item column {i + 1}.");
            var last = sheet.LastRowUsed()?.RowNumber() ?? ItemHeaderRow;
            if (last >= FirstItemRow + MaxItems) throw new ExcelValidationException($"No more than {MaxItems} inspection items are allowed.");
            if (sheet.LastColumnUsed()?.ColumnNumber() > ItemLabels.Length)
                throw new ExcelValidationException("Unexpected columns found in workbook.");
            foreach (var cell in sheet.CellsUsed())
                if (cell.HasFormula) throw new ExcelValidationException("Formulas are not allowed in the import workbook.");
            var items = new List<ItemInput>();
            for (var row = FirstItemRow; row <= last; row++)
            {
                var f = Enumerable.Range(1, ItemLabels.Length).Select(col => sheet.Cell(row, col).GetString().Trim()).ToArray();
                if (f.All(string.IsNullOrWhiteSpace)) continue;
                items.Add(new ItemInput(f[0], f[1], f[2], f[3], f[4], f[5], f[6], f[7], f[8], f[9], f[10], f[11], f[12], f[13]));
            }
            string Header(int row) => sheet.Cell(row, 2).GetString().Trim();
            return new StandardInput(Header(6), Header(1), Header(2), Header(3), Header(4), Header(5), Header(7), items);
        }
        catch (ExcelValidationException) { throw; }
        catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
        {
            throw new ExcelValidationException("The uploaded file is not a valid Patrol Standard .xlsx workbook.");
        }
    }
}

public sealed class ExcelValidationException(string message) : Exception(message);
