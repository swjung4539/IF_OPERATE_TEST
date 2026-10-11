using OperateTest.App.Models;
using OperateTest.App.Services;
using TestEmulator.Controls;
using TestEmulator.Theme;

namespace OperateTest.App.Forms;

public sealed class InspectionSetupForm : Form
{
    private static readonly Dictionary<InspectionBoardType, SetupDefaults> LastSettings = [];
    private readonly InspectionBoardType _boardType;
    private readonly InspectionWorkbookService _workbooks = new();
    private readonly InspectionCheckpointService _checkpoints = new();
    private readonly AppTextBox _templatePath = new() { ReadOnly = true };
    private readonly AppTextBox _inspector = new();
    private readonly AppTextBox _serial = new();
    private readonly AppTextBox _outputDirectory = new();
    private readonly DateTimePicker _inspectionDate = new();
    private readonly Label _validationStatus = new();
    private readonly AppButton _start = new();
    private readonly ToolTip _toolTip = new() { AutoPopDelay = 12000, InitialDelay = 400, ReshowDelay = 100 };

    public InspectionSetupForm(InspectionBoardType boardType)
    {
        _boardType = boardType;
        var boardName = boardType == InspectionBoardType.Communication ? "COMM" : "DIO";

        Text = $"{boardName} 검사 준비";
        AutoScaleMode = AutoScaleMode.None;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(900, 660);
        BackColor = Colors.AppBackground;
        Font = Fonts.Body;

        var header = new Panel { Dock = DockStyle.Top, Height = 96, BackColor = Colors.Surface };
        header.Controls.Add(new Label
        {
            Location = new Point(30, 18), Size = new Size(700, 34), Font = Fonts.PageTitle,
            ForeColor = Colors.TextPrimary, Text = $"{boardName} 수입검사 준비"
        });
        header.Controls.Add(new Label
        {
            Location = new Point(32, 55), Size = new Size(810, 23), Font = Fonts.Caption,
            ForeColor = Colors.TextSecondary,
            Text = "기준서 원본은 수정하지 않으며 결과용 사본에 검사 정보를 기록합니다."
        });
        Controls.Add(header);

        var card = new AppPanel { Location = new Point(30, 120), Size = new Size(840, 458) };
        card.Controls.Add(SectionTitle("1. 수입검사 기준서", 24, 18));
        card.Controls.Add(Caption("지원하는 고정 양식의 .xlsx 파일을 선택하세요.", 24, 47, 600));
        _templatePath.SetBounds(24, 77, 650, 38);
        var selectTemplate = new AppButton
        {
            Variant = AppButtonVariant.OutlinePrimary, IconGlyph = AppIcon.FolderOpen,
            Text = "파일 선택", Location = new Point(690, 76), Size = new Size(126, 40)
        };
        selectTemplate.Click += (_, _) => SelectTemplate();
        card.Controls.Add(_templatePath);
        card.Controls.Add(selectTemplate);

        _validationStatus.SetBounds(24, 124, 792, 25);
        _validationStatus.Font = Fonts.Caption;
        _validationStatus.ForeColor = Colors.TextMuted;
        _validationStatus.Text = "기준서를 선택하면 양식을 자동으로 확인합니다.";
        card.Controls.Add(_validationStatus);

        card.Controls.Add(SectionTitle("2. 검사 정보", 24, 168));
        card.Controls.Add(FieldLabel("검사일자", 24, 207));
        card.Controls.Add(FieldLabel("검사자", 294, 207));
        card.Controls.Add(FieldLabel("시리얼번호", 564, 207));

        _inspectionDate.SetBounds(24, 234, 246, 38);
        _inspectionDate.Format = DateTimePickerFormat.Custom;
        _inspectionDate.CustomFormat = "yyyy.MM.dd";
        _inspectionDate.Font = Fonts.Body;
        _inspector.SetBounds(294, 234, 246, 38);
        _serial.SetBounds(564, 234, 252, 38);
        card.Controls.Add(_inspectionDate);
        card.Controls.Add(_inspector);
        card.Controls.Add(_serial);

        card.Controls.Add(Caption(
            $"시리얼 형식 예: {InspectionWorkbookService.ExpectedSerialExample(boardType)}",
            564, 278, 252));

        card.Controls.Add(SectionTitle("3. 결과 저장 폴더", 24, 316));
        card.Controls.Add(Caption("동일 날짜·검사자 결과 파일이 있으면 다음 빈 행에 이어서 기록합니다.", 24, 345, 650));
        _outputDirectory.SetBounds(24, 375, 650, 38);
        var selectOutput = new AppButton
        {
            Variant = AppButtonVariant.Secondary, IconGlyph = AppIcon.FolderOpen,
            Text = "폴더 선택", Location = new Point(690, 374), Size = new Size(126, 40)
        };
        selectOutput.Click += (_, _) => SelectOutputDirectory();
        card.Controls.Add(_outputDirectory);
        card.Controls.Add(selectOutput);
        Controls.Add(card);

        var resume = new AppButton
        {
            Variant = AppButtonVariant.OutlinePrimary, IconGlyph = AppIcon.FolderOpen,
            Text = "중단 검사 이어서", Location = new Point(370, 596), Size = new Size(200, 44)
        };
        resume.Click += (_, _) => ResumeInspection();
        var cancel = new AppButton
        {
            Variant = AppButtonVariant.Secondary, Text = "취소",
            Location = new Point(586, 596), Size = new Size(132, 44)
        };
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        _start.Variant = boardType == InspectionBoardType.Communication
            ? AppButtonVariant.Primary
            : AppButtonVariant.Warning;
        _start.IconGlyph = AppIcon.ChevronRight;
        _start.Text = "검사 시작";
        _start.SetBounds(734, 596, 136, 44);
        _start.Enabled = false;
        _start.Click += (_, _) => CreateSession();
        Controls.Add(cancel);
        Controls.Add(resume);
        Controls.Add(_start);
        ApplyLastSettings();
    }

    public InspectionSession? Session { get; private set; }

    private void SelectTemplate()
    {
        using var dialog = new OpenFileDialog
        {
            Title = _boardType == InspectionBoardType.Communication
                ? "COMM 수입검사 기준서 선택"
                : "DIO 수입검사 기준서 선택",
            Filter = "Excel 통합 문서 (*.xlsx)|*.xlsx",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        _templatePath.TextValue = dialog.FileName;
        _templatePath.SetToolTip(_toolTip, dialog.FileName);
        if (string.IsNullOrWhiteSpace(_outputDirectory.TextValue))
            _outputDirectory.TextValue = Path.Combine(Path.GetDirectoryName(dialog.FileName)!, "검사결과");
        _outputDirectory.SetToolTip(_toolTip, _outputDirectory.TextValue);

        var validation = _workbooks.Validate(dialog.FileName, _boardType);
        _validationStatus.Text = validation.IsValid
            ? $"지원하는 수입검사 기준서입니다. ({Path.GetFileName(dialog.FileName)})"
            : validation.Message;
        _validationStatus.ForeColor = validation.IsValid ? Colors.Success : Colors.Danger;
        _templatePath.HasError = !validation.IsValid;
        _start.Enabled = validation.IsValid;
    }

    private void SelectOutputDirectory()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "검사 결과 Excel 파일을 저장할 폴더를 선택하세요.",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true
        };
        if (Directory.Exists(_outputDirectory.TextValue))
            dialog.InitialDirectory = _outputDirectory.TextValue;
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _outputDirectory.TextValue = dialog.SelectedPath;
            _outputDirectory.SetToolTip(_toolTip, dialog.SelectedPath);
        }
    }

    private void CreateSession()
    {
        _inspector.HasError = string.IsNullOrWhiteSpace(_inspector.TextValue);
        _serial.HasError = string.IsNullOrWhiteSpace(_serial.TextValue);
        _outputDirectory.HasError = string.IsNullOrWhiteSpace(_outputDirectory.TextValue);
        if (_inspector.HasError || _serial.HasError || _outputDirectory.HasError)
        {
            MessageBox.Show(this, "검사자, 시리얼번호, 결과 저장 폴더를 모두 입력하세요.",
                "검사 정보 확인", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _start.Enabled = false;
        try
        {
            Session = _workbooks.CreateSession(
                _templatePath.TextValue,
                _outputDirectory.TextValue.Trim(),
                _boardType,
                _inspectionDate.Value.Date,
                _inspector.TextValue,
                _serial.TextValue);
            _checkpoints.Save(Session);
            RememberSettings(Session);
            MessageBox.Show(this,
                $"검사 결과 파일을 준비했습니다.{Environment.NewLine}{Environment.NewLine}" +
                $"파일: {Path.GetFileName(Session.ResultPath)}{Environment.NewLine}" +
                $"기록 번호: {Session.RowNumber - 5}{Environment.NewLine}" +
                $"시리얼: {Session.SerialNumber}",
                "검사 준비 완료", MessageBoxButtons.OK, MessageBoxIcon.Information);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "검사 준비 실패", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _start.Enabled = true;
        }
    }

    private void ResumeInspection()
    {
        using var dialog = new OpenFileDialog
        {
            Title = $"{(_boardType == InspectionBoardType.Communication ? "COMM" : "DIO")} 중단 검사 체크포인트 선택",
            Filter = "검사 체크포인트 (*.inspection.json)|*.inspection.json|JSON 파일 (*.json)|*.json",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            Session = _checkpoints.Load(dialog.FileName, _boardType);
            RememberSettings(Session);
            MessageBox.Show(this,
                $"중단된 검사를 불러왔습니다.{Environment.NewLine}{Environment.NewLine}" +
                $"시리얼: {Session.SerialNumber}{Environment.NewLine}" +
                $"검사자: {Session.Inspector}{Environment.NewLine}" +
                $"기록 번호: {Session.RowNumber - 5}{Environment.NewLine}{Environment.NewLine}" +
                "중단 당시 실행 중이던 항목은 STOP 상태로 복구됩니다.",
                "검사 이어서", MessageBoxButtons.OK, MessageBoxIcon.Information);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            Session = null;
            MessageBox.Show(this, ex.Message, "체크포인트 불러오기 실패", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void ApplyLastSettings()
    {
        if (!LastSettings.TryGetValue(_boardType, out var settings)) return;
        _templatePath.TextValue = settings.TemplatePath;
        _templatePath.SetToolTip(_toolTip, settings.TemplatePath);
        _outputDirectory.TextValue = settings.OutputDirectory;
        _outputDirectory.SetToolTip(_toolTip, settings.OutputDirectory);
        _inspector.TextValue = settings.Inspector;
        _inspectionDate.Value = settings.InspectionDate;
        var validation = _workbooks.Validate(settings.TemplatePath, _boardType);
        _validationStatus.Text = validation.IsValid
            ? $"지원하는 수입검사 기준서입니다. ({Path.GetFileName(settings.TemplatePath)})"
            : validation.Message;
        _validationStatus.ForeColor = validation.IsValid ? Colors.Success : Colors.Danger;
        _templatePath.HasError = !validation.IsValid;
        _start.Enabled = validation.IsValid;
    }

    private static void RememberSettings(InspectionSession session)
    {
        LastSettings[session.BoardType] = new SetupDefaults(
            session.TemplatePath,
            Path.GetDirectoryName(session.ResultPath)!,
            session.Inspector,
            session.InspectionDate);
    }

    private sealed record SetupDefaults(
        string TemplatePath,
        string OutputDirectory,
        string Inspector,
        DateTime InspectionDate);

    private static Label SectionTitle(string text, int x, int y) => new()
    {
        Location = new Point(x, y), Size = new Size(500, 26), Font = Fonts.SectionTitle,
        ForeColor = Colors.TextPrimary, Text = text
    };

    private static Label FieldLabel(string text, int x, int y) => new()
    {
        Location = new Point(x, y), Size = new Size(240, 22), Font = Fonts.Caption,
        ForeColor = Colors.TextSecondary, Text = text
    };

    private static Label Caption(string text, int x, int y, int width) => new()
    {
        Location = new Point(x, y), Size = new Size(width, 22), Font = Fonts.Caption,
        ForeColor = Colors.TextSecondary, Text = text, AutoEllipsis = true
    };
}
