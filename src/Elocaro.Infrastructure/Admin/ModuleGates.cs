using Elocaro.Application.Admin;
using Quartz;

namespace Elocaro.Infrastructure.Admin;

/// <summary>Vetoes jobs of switched-off modules, so no job needs its own check.</summary>
internal sealed class ModuleJobGate(IModuleState state) : ITriggerListener
{
    public string Name => nameof(ModuleJobGate);

    public ValueTask<bool> VetoJobExecution(ITrigger trigger, IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        var name = context.JobDetail.Key.Name;
        var moduleOff = ModuleCatalog.OwnerOfJob(name) is { } owner && !state.IsEnabled(owner.Module);
        var optionOff = ModuleCatalog.OptionOfJob(name) is { } option && !state.IsVisible(option.Option);
        return ValueTask.FromResult(moduleOff || optionOff);
    }

    public ValueTask TriggerFired(ITrigger trigger, IJobExecutionContext context, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    public ValueTask TriggerMisfired(ITrigger trigger, IScheduler scheduler, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    public ValueTask TriggerComplete(ITrigger trigger, IJobExecutionContext context, SchedulerInstruction triggerInstructionCode, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
}
