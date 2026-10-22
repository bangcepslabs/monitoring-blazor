using Microsoft.AspNetCore.Components;

namespace Monitoring.Blazor.Components;

// Cascading updates refresh each page without recreating it or losing uploaded logs.
public abstract class LocalizedComponentBase : ComponentBase
{
    [CascadingParameter(Name = "UiLanguage")]
    public string UiLanguage { get; set; } = "ko";
}
