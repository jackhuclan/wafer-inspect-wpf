#nullable enable
using WaferInspect.Core.Maps;
using WaferInspect.Core.Recipes;
using Xunit;

namespace WaferInspect.Core.Tests;

/// <summary>
/// P2（H02）配方与 Job 管理：CRUD、必填校验、BR-15/16/17/78 与 Map 导入契约。
/// 设备与真实持久化不在本测试范围（T-029 / H07 未闭环）。
/// </summary>
public sealed class JobRepositoryTests
{
    private static InMemoryJobRepository NewRepository()
        => new(new SimulatedMapTypeCatalog());

    private static JobDefinition ValidJob(string name = "J-TEST")
    {
        string mapPath = Path.GetTempFileName();   // .tmp 扩展名，下面改写为 .csv
        string csvPath = Path.ChangeExtension(mapPath, ".csv");
        File.Move(mapPath, csvPath, true);
        File.WriteAllText(csvPath, "DieX,DieY,Cols,Rows\n24,25,10,10\n0,0,1\n");

        return new JobDefinition
        {
            Name = name,
            PatternType = WaferPatternType.PatternWithMark,
            Size = WaferSize.Inch12,
            DieXmm = 24,
            DieYmm = 25,
            MapTypeId = "MAP-PL-300",
            MapFileType = MapFileType.Csv,
            MapFilePath = csvPath,
            BarcodeCode = "1"
        };
    }

    [Fact]
    public void Create_ValidJob_AssignsIdAndAppearsInSnapshot()
    {
        var repo = NewRepository();
        int before = repo.Jobs.Count;

        var created = repo.Create(ValidJob());

        Assert.StartsWith("JOB-", created.JobId);
        Assert.Equal(before + 1, repo.Jobs.Count);
        Assert.Contains(repo.Jobs, j => j.JobId == created.JobId);
    }

    [Fact]
    public void Create_DuplicateName_IsRejected()
    {
        var repo = NewRepository();
        string existing = repo.Jobs[0].Name;

        var ex = Assert.Throws<JobValidationException>(() => repo.Create(ValidJob(existing)));

        Assert.Contains("名称已存在", ex.Message);
    }

    [Fact]
    public void Create_MissingRequiredFields_ReportsEveryIssue()
    {
        var repo = NewRepository();
        var draft = new JobDefinition();   // 全空

        var ex = Assert.Throws<JobValidationException>(() => repo.Create(draft));

        Assert.Contains(ex.Issues, i => i.Field == nameof(JobDefinition.Name));
        Assert.Contains(ex.Issues, i => i.Field == nameof(JobDefinition.DieYmm));
        Assert.Contains(ex.Issues, i => i.Field == nameof(JobDefinition.MapTypeId));
        Assert.Contains(ex.Issues, i => i.Field == nameof(JobDefinition.MapFilePath));
        Assert.Contains(ex.Issues, i => i.Field == nameof(JobDefinition.BarcodeCode));
    }

    [Fact]
    public void BR16_DefaultPatternType_IsPatternWithMark()
    {
        Assert.Equal(WaferPatternType.PatternWithMark, new JobDefinition().PatternType);
        Assert.Equal(3, Enum.GetValues<WaferPatternType>().Length);
    }

    [Fact]
    public void BR17_DefaultNotchAngle_Is270()
    {
        Assert.Equal(270, new JobDefinition().NotchAngle);
    }

    [Fact]
    public void BR78_DeclaredTypeMustMatchFileExtension()
    {
        var job = ValidJob();
        job.MapFileType = MapFileType.Txt;   // 实际是 .csv

        var issues = JobValidator.Validate(job);

        Assert.Contains(issues, i => i.Field == nameof(JobDefinition.MapFileType) && i.Message.Contains("BR-78"));
    }

    [Fact]
    public void BR78_Importer_RejectsMismatchedExtension()
    {
        var job = ValidJob();
        var importer = new MapFileImporter();

        var result = importer.Import(job.MapFilePath, MapFileType.Txt);

        Assert.False(result.Success);
        Assert.Contains("BR-78", result.Error);
    }

    [Fact]
    public void Importer_Csv_ReturnsDieSizeGridAndCodes()
    {
        var job = ValidJob();
        var importer = new MapFileImporter();

        var result = importer.Import(job.MapFilePath, MapFileType.Csv);

        Assert.True(result.Success);
        Assert.Equal(24, result.DieXmm);
        Assert.Equal(25, result.DieYmm);
        Assert.Equal(10, result.Columns);
        Assert.Equal(10, result.Rows);
        Assert.Contains("1", result.BarcodeCodes);
    }

    [Fact]
    public void Importer_Txt_ReturnsDieSizeGridAndCodes()
    {
        string path = Path.ChangeExtension(Path.GetTempFileName(), ".txt");
        File.WriteAllText(path, "DieX=32\nDieY=22\nCols=40\nRows=30\n0,0,1\n0,1,2\n");

        var result = new MapFileImporter().Import(path, MapFileType.Txt);

        Assert.True(result.Success);
        Assert.Equal(32, result.DieXmm);
        Assert.Equal(2, result.BarcodeCodes.Count);
    }

    [Fact]
    public void Importer_OutOfRangeDie_Fails()
    {
        string path = Path.ChangeExtension(Path.GetTempFileName(), ".csv");
        File.WriteAllText(path, "DieX,DieY,Cols,Rows\n24,25,10,10\n99,0,1\n");

        var result = new MapFileImporter().Import(path, MapFileType.Csv);

        Assert.False(result.Success);
        Assert.Contains("越界", result.Error);
    }

    [Fact]
    public void Importer_UnsupportedFormat_ReportsExplicitly()
    {
        string path = Path.ChangeExtension(Path.GetTempFileName(), ".xml");
        File.WriteAllText(path, "<map/>");

        var result = new MapFileImporter().Import(path, MapFileType.Xml);

        Assert.False(result.Success);
        Assert.Contains("解析器未接入", result.Error);
    }

    [Fact]
    public void Duplicate_CopiesAllParameters_AndKeepsNamesUnique()
    {
        var repo = NewRepository();
        var source = repo.Jobs[0];

        var copy = repo.Duplicate(source.JobId);

        Assert.NotEqual(source.JobId, copy.JobId);
        Assert.NotEqual(source.Name, copy.Name);
        Assert.Equal(source.DieXmm, copy.DieXmm);
        Assert.Equal(source.MapFilePath, copy.MapFilePath);
        Assert.Equal(source.TraceImage, copy.TraceImage);

        // 再次复制同一 Job 不得撞名
        var second = repo.Duplicate(source.JobId);
        Assert.NotEqual(copy.Name, second.Name);
    }

    [Fact]
    public void Rename_DuplicateName_IsRejected()
    {
        var repo = NewRepository();
        var target = repo.Jobs[1];

        Assert.Throws<JobValidationException>(() => repo.Rename(target.JobId, repo.Jobs[0].Name));
    }

    [Fact]
    public void Rename_UpdatesNameAndResorts()
    {
        var repo = NewRepository();
        var target = repo.Jobs[0];

        var renamed = repo.Rename(target.JobId, "000-NEW-NAME");

        Assert.Equal("000-NEW-NAME", renamed.Name);
        Assert.Equal(target.JobId, repo.Jobs[0].JobId);   // 名称排序后置顶
    }

    [Fact]
    public void Delete_RemovesJob_AndUnknownIdThrows()
    {
        var repo = NewRepository();
        string id = repo.Jobs[0].JobId;
        int before = repo.Jobs.Count;

        repo.Delete(id);

        Assert.Equal(before - 1, repo.Jobs.Count);
        Assert.Null(repo.Find(id));
        Assert.Throws<InvalidOperationException>(() => repo.Delete(id));
    }

    [Fact]
    public void BR15_TraceImage_IsJobLevelAndNullable()
    {
        var repo = NewRepository();
        var job = repo.Create(ValidJob("J-BR15"));
        Assert.Null(job.TraceImage);   // 未勾选 = 继承全局

        job.TraceImage = TraceImagePolicy.UploadDefectOnly;
        repo.Update(job);

        Assert.Equal(TraceImagePolicy.UploadDefectOnly, repo.Find(job.JobId)!.TraceImage);
    }

    [Fact]
    public void IgnoredByRecommendationFields_AreEditableAndPersisted()
    {
        // F-3.2.1-22~27：手册标「推荐忽略」，级 C 保留 —— 必须可写可读，不得丢弃
        var repo = NewRepository();
        var job = repo.Create(ValidJob("J-IGNORED"));
        job.LocateDieX = 3;
        job.LocateDieY = 4;
        job.Orientation = "90";
        job.ColumnOffset = 1;
        job.RowOffset = -1;
        job.ColRow = "C3R4";
        job.Xy = "12,14";
        job.IncludeExclude = "include:1-9";

        repo.Update(job);

        var stored = repo.Find(job.JobId)!;
        Assert.Equal(3, stored.LocateDieX);
        Assert.Equal(4, stored.LocateDieY);
        Assert.Equal("90", stored.Orientation);
        Assert.Equal(1, stored.ColumnOffset);
        Assert.Equal(-1, stored.RowOffset);
        Assert.Equal("C3R4", stored.ColRow);
        Assert.Equal("12,14", stored.Xy);
        Assert.Equal("include:1-9", stored.IncludeExclude);
    }

    [Fact]
    public void Update_UnknownId_ThrowsInsteadOfSilentlyCreating()
    {
        var repo = NewRepository();
        var orphan = ValidJob("J-ORPHAN");
        orphan.JobId = "JOB-9999";

        Assert.Throws<InvalidOperationException>(() => repo.Update(orphan));
    }

    [Fact]
    public void MapTypeCatalog_ProvidesEightBins_ForJobSelection()
    {
        var catalog = new SimulatedMapTypeCatalog();

        Assert.NotEmpty(catalog.Types);
        Assert.All(catalog.Types, t => Assert.Equal(8, t.Bins.Count));
        Assert.All(catalog.Types, t => Assert.All(t.Bins, b => Assert.False(string.IsNullOrWhiteSpace(b.Shortcut))));
        Assert.NotNull(catalog.Find(catalog.Types[0].Id));
    }
}
