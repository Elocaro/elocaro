namespace Elocaro.Web.Components.Controls;

/// <summary>Color role of a chart mark; the colors themselves live in app.css.</summary>
public enum ChartTone
{
    Primary,
    Tertiary,
    Success,
    Warning,
    Error,
    Neutral,
}

/// <summary>A part of a <see cref="SegmentBar"/>. The text carries the value, the color alone never does.</summary>
public sealed record ChartSegment(string Label, decimal Value, string ValueText, ChartTone Tone);

/// <summary>A row of <see cref="CostBars"/>.</summary>
public sealed record ChartBar(string Label, decimal Amount);

/// <summary>A point of a <see cref="LineChart"/>. A <paramref name="Marker"/> draws a vertical line and appears in the legend.</summary>
public sealed record ChartPoint(string Label, decimal Value, string? Marker = null);

/// <summary>State of a step in a <see cref="ProcessTrack"/>.</summary>
public enum ProcessState
{
    Open,
    Current,
    Done,

    /// <summary>The application cannot tell whether this step was done; it is offered as a link without a check mark.</summary>
    Unknown,
}

/// <summary>A step of a process: where the user stands and where to go next.</summary>
public sealed record ProcessStep(string Title, ProcessState State, string? Detail = null, string? Href = null);
