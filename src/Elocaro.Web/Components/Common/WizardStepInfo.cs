namespace Elocaro.Web.Components.Common;

/// <summary>One step of a <see cref="Wizard"/>.</summary>
/// <param name="Title">Heading of the step; also its name in the step list.</param>
/// <param name="Section">
/// Steps with the same section that follow each other are grouped in the step list and show their sub-steps only while one of them is
/// current. Without a section the step stands for itself.
/// </param>
/// <param name="Description">Optional sentence below the heading.</param>
/// <param name="Badge">Short marker in the step list, e.g. the number of open findings; null = none.</param>
/// <param name="BadgeLabel">Spoken form of the badge for screen readers, e.g. "2 Hinweise".</param>
public sealed record WizardStepInfo(string Title, string? Section = null, string? Description = null, string? Badge = null, string? BadgeLabel = null);
