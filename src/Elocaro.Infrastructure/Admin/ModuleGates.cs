using Elocaro.Application.Admin;
using Quartz;

namespace Elocaro.Infrastructure.Admin;

/// <summary>Vetoes jobs of switched-off modules, so no job needs its own check.</summary>
internal sealed class ModuleJobGate(IModuleState state) : ITriggerListener
{
    public string Name => nameof(ModuleJobGate);

    public Task<bool> VetoJobExecution(ITrigger trigger, IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        var name = context.JobDetail.Key.Name;
        var moduleOff = ModuleCatalog.OwnerOfJob(name) is { } owner && !state.IsEnabled(owner.Module);
        var optionOff = ModuleCatalog.OptionOfJob(name) is { } option && !state.IsVisible(option.Option);
        return Task.FromResult(moduleOff || optionOff);
    }

    public Task TriggerFired(ITrigger trigger, IJobExecutionContext context, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task TriggerMisfired(ITrigger trigger, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task TriggerComplete(ITrigger trigger, IJobExecutionContext context, SchedulerInstruction triggerInstructionCode, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
