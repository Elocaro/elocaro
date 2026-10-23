namespace MietHahn.Web.Components.Common;

/// <summary>One step of a <see cref="Wizard"/>.</summary>
/// <param name="Title">Heading of the step; also its name in the step list.</param>
/// <param name="Section">
/// Steps with the same section that follow each other are grouped in the step list and show their sub-steps only while one of them is
/// current. Without a section the step stands for itself.
/// </param>
/// <param name="Description">Optional sentence below the heading.</param>
public sealed record WizardStepInfo(string Title, string? Section = null, string? Description = null);
