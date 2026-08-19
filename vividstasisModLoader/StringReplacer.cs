using UndertaleModLib;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;

namespace vividstasisModLoader;

public class StringReplacer(UndertaleData data,string modDir)
{
    private readonly string _excelPath = $"{modDir}/excel";
    private readonly Dictionary<string, string> _replaceDict = new();

    public bool Exist()
    {
        return Directory.Exists(_excelPath);
    }

    public void Execute()
    {
        if(!Exist()) return;
        foreach (var file in Directory.GetFiles(_excelPath, "*.xlsx"))
        {
            using var stream = new FileStream(file, FileMode.Open);
            var workbook = new XSSFWorkbook(stream);
            var sheet = workbook.GetSheetAt(0);

            for (var row = 0; row <= sheet.LastRowNum; row++)
            {
                var currentRow = sheet.GetRow(row);
                if (currentRow == null) continue;
                if (currentRow.Cells.Count < 2) continue;
                
                _replaceDict.TryAdd(currentRow.Cells[0].ToString(), string.IsNullOrEmpty(currentRow.Cells[1].ToString())? currentRow.Cells[0].ToString(): currentRow.Cells[1].ToString());
            }
        }

        foreach (var str in data.Strings)
        {
            if (_replaceDict.TryGetValue(str.Content, out var value))
            {
                str.Content = value;
            }
        }
    }
}