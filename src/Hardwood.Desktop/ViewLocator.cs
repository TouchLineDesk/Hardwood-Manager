using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Hardwood.Desktop.ViewModels;

namespace Hardwood.Desktop;

/// <summary>Maps FooViewModel → Views.FooView by name.</summary>
public class ViewLocator : IDataTemplate
{
    public Control? Build(object? data)
    {
        if (data is null) return null;
        var name = data.GetType().FullName!.Replace("ViewModels", "Views").Replace("ViewModel", "View");
        var type = Type.GetType(name);
        return type != null ? (Control)Activator.CreateInstance(type)! : new TextBlock { Text = "Not found: " + name };
    }

    public bool Match(object? data) => data is ViewModelBase;
}
