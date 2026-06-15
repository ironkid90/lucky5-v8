namespace Lucky5.Infrastructure.Services;

public sealed class MachineCacheTtlOptions
{
    public TimeSpan ActiveRoundTtl { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan MachineSessionTtl { get; set; } = TimeSpan.FromSeconds(15);
}
