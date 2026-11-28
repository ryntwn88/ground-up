using System.Text;
using Godot;
using GroundUp.Core.Scoring;

namespace GroundUp.GodotClient;

/// <summary>
/// Celebratory First Free Ascent (FFA) completion modal where the route developer names the new line,
/// selects a consensus YDS grade, and inspects the 1.0-4.0 Star Rating breakdown and climber critiques.
/// </summary>
public partial class RouteCompletionModal : CanvasLayer
{
    private static readonly string[] YdsGrades =
    [
        "5.7", "5.8", "5.9", "5.10a", "5.10b", "5.10c", "5.10d",
        "5.11a", "5.11b", "5.11c", "5.11d", "5.12a"
    ];

    private PanelContainer _backdrop = null!;
    private LineEdit _routeNameInput = null!;
    private OptionButton _gradeSelector = null!;
    private Label _starsHeaderLabel = null!;
    private Label _subscoresLabel = null!;
    private Label _feedbackNotesLabel = null!;
    private Label _savedBannerLabel = null!;

    public bool IsOpen => _backdrop.Visible;

    public event Action? ClimbAgainFfaRequested;
    public event Action? ReturnToDevRequested;
    public event Action? WipeFreshWallRequested;

    public override void _Ready()
    {
        Layer = 12;
        BuildModalUi();
    }

    public void ShowEvaluation(RouteEvaluationReport report, string defaultName = "Granite Graduation")
    {
        _routeNameInput.Text = defaultName;
        _savedBannerLabel.Text = string.Empty;

        int gradeIdx = Array.IndexOf(YdsGrades, report.SuggestedYdsGrade);
        _gradeSelector.Selected = gradeIdx >= 0 ? gradeIdx : 4;

        int fullStars = Mathf.Clamp((int)MathF.Round(report.StarRating), 1, 4);
        string starGlyphs = new string('★', fullStars) + new string('☆', 4 - fullStars);
        _starsHeaderLabel.Text = $"{starGlyphs}   {report.StarRating:F1} / 4.0 STARS   (Est. Difficulty: {report.SuggestedYdsGrade})";

        _subscoresLabel.Text =
            $"Safety: {report.SafetyScore:F0}/100   |   Clipping Ergonomics: {report.ErgonomicsScore:F0}/100   |   " +
            $"Bolting Ethics: {report.EthicsScore:F0}/100   |   Rock Prep: {report.RockQualityScore:F0}/100";

        var sb = new StringBuilder();
        sb.AppendLine("Climber Consensus & Route Feedback:");
        foreach (string note in report.FeedbackNotes)
        {
            sb.AppendLine($" • {note}");
        }

        _feedbackNotesLabel.Text = sb.ToString();
        _backdrop.Visible = true;
    }

    public void CloseModal()
    {
        _backdrop.Visible = false;
    }

    private void BuildModalUi()
    {
        var cardStyle = new StyleBoxFlat
        {
            BgColor = new Color(0.07f, 0.09f, 0.13f, 0.96f),
            BorderWidthLeft = 2,
            BorderWidthRight = 2,
            BorderWidthTop = 2,
            BorderWidthBottom = 2,
            BorderColor = new Color(0.95f, 0.78f, 0.24f),
            CornerRadiusTopLeft = 10,
            CornerRadiusTopRight = 10,
            CornerRadiusBottomLeft = 10,
            CornerRadiusBottomRight = 10,
            ContentMarginLeft = 26,
            ContentMarginRight = 26,
            ContentMarginTop = 22,
            ContentMarginBottom = 22
        };

        _backdrop = new PanelContainer
        {
            AnchorLeft = 0.5f,
            AnchorRight = 0.5f,
            AnchorTop = 0.5f,
            AnchorBottom = 0.5f,
            OffsetLeft = -360f,
            OffsetRight = 360f,
            OffsetTop = -240f,
            OffsetBottom = 240f,
            Visible = false
        };
        _backdrop.AddThemeStyleboxOverride("panel", cardStyle);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 12);

        var title = new Label
        {
            Text = "FIRST FREE ASCENT (FFA) CLAIMED!",
            HorizontalAlignment = HorizontalAlignment.Center
        };
        title.AddThemeColorOverride("font_color", new Color(0.98f, 0.86f, 0.30f));

        _starsHeaderLabel = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center
        };
        _starsHeaderLabel.AddThemeColorOverride("font_color", new Color(0.32f, 0.96f, 0.65f));

        _subscoresLabel = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center
        };
        _subscoresLabel.AddThemeColorOverride("font_color", new Color(0.76f, 0.86f, 0.96f));

        var formRow = new HBoxContainer();
        formRow.AddThemeConstantOverride("separation", 12);
        formRow.AddChild(new Label { Text = "Route Name:" });

        _routeNameInput = new LineEdit
        {
            CustomMinimumSize = new Vector2(280f, 32f),
            PlaceholderText = "Enter route name..."
        };
        formRow.AddChild(_routeNameInput);

        formRow.AddChild(new Label { Text = "Proposed Grade:" });
        _gradeSelector = new OptionButton { CustomMinimumSize = new Vector2(110f, 32f) };
        foreach (string g in YdsGrades)
        {
            _gradeSelector.AddItem(g);
        }

        formRow.AddChild(_gradeSelector);

        var registerBtn = new Button { Text = "Log FFA in Guidebook" };
        registerBtn.Pressed += () =>
        {
            string chosenGrade = _gradeSelector.GetItemText(_gradeSelector.Selected);
            string chosenName = string.IsNullOrWhiteSpace(_routeNameInput.Text) ? "Unnamed Line" : _routeNameInput.Text.Trim();
            _savedBannerLabel.Text = $"✓ Logged FFA: '{chosenName}' ({chosenGrade})!";
        };
        formRow.AddChild(registerBtn);

        _savedBannerLabel = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center
        };
        _savedBannerLabel.AddThemeColorOverride("font_color", new Color(0.45f, 0.98f, 0.58f));

        _feedbackNotesLabel = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(650f, 120f)
        };

        var actionRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        actionRow.AddThemeConstantOverride("separation", 14);

        var againBtn = new Button { Text = "Re-Climb FFA Redpoint" };
        againBtn.Pressed += () =>
        {
            CloseModal();
            ClimbAgainFfaRequested?.Invoke();
        };

        var devBtn = new Button { Text = "Return to Route Development" };
        devBtn.Pressed += () =>
        {
            CloseModal();
            ReturnToDevRequested?.Invoke();
        };

        var wipeBtn = new Button { Text = "Fresh Untouched Cliff" };
        wipeBtn.Pressed += () =>
        {
            CloseModal();
            WipeFreshWallRequested?.Invoke();
        };

        var closeBtn = new Button { Text = "Close" };
        closeBtn.Pressed += CloseModal;

        actionRow.AddChild(againBtn);
        actionRow.AddChild(devBtn);
        actionRow.AddChild(wipeBtn);
        actionRow.AddChild(closeBtn);

        vbox.AddChild(title);
        vbox.AddChild(_starsHeaderLabel);
        vbox.AddChild(_subscoresLabel);
        vbox.AddChild(formRow);
        vbox.AddChild(_savedBannerLabel);
        vbox.AddChild(_feedbackNotesLabel);
        vbox.AddChild(actionRow);

        _backdrop.AddChild(vbox);
        AddChild(_backdrop);
    }
}
