using System.Windows;
using System.Windows.Controls;
using Nebula.ViewModels;

namespace Nebula.Views;

/// <summary>
/// 세션 타입에 따라 적절한 DataTemplate 선택
/// </summary>
public class SessionDataTemplateSelector : DataTemplateSelector
{
    /// <summary>
    /// 세션 선택 화면용 DataTemplate
    /// </summary>
    public DataTemplate? SelectorTemplate { get; set; }

    /// <summary>
    /// SSH 세션용 DataTemplate
    /// </summary>
    public DataTemplate? SshSessionTemplate { get; set; }

    /// <summary>
    /// 로컬 터미널 세션용 DataTemplate
    /// </summary>
    public DataTemplate? LocalSessionTemplate { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container)
    {
        if (item is NewSessionSelectorViewModel)
        {
            return SelectorTemplate;
        }
        else if (item is ServerSessionViewModel)
        {
            return SshSessionTemplate;
        }
        else if (item is LocalTerminalViewModel)
        {
            return LocalSessionTemplate;
        }

        return base.SelectTemplate(item, container);
    }
}
