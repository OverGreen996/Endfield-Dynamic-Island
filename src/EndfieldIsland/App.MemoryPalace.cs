using EndfieldChargePlus.Views;
using EndfieldChargePlus.Interop;

namespace EndfieldChargePlus;

public partial class App
{
    private MemoryPalaceWindow? _memoryPalace;
    private bool _memoryOpening;
    private async void OpenMemoryPalace()
    {
        if(_runtime is null||_notificationsExiting||_memoryOpening||_assistantOpening||_musicOpening)return;
        if(_notificationOpening||_notificationIsland?.IsVisible==true){_notificationReturn=NotificationReturn.Memory;return;}
        _memoryOpening=true;
        try
        {
            if(_assistant?.IsVisible==true)await _assistant.HideAnimatedAsync();
            if(_music?.IsVisible==true){await IslandTransition.FadeOutAsync(_music);_music.HideIsland();}
            await _runtime.BeginAssistantAsync();
            _memoryPalace??=CreateMemoryPalace();
            IslandTransition.PrepareShow(_memoryPalace);_memoryPalace.Show();_memoryPalace.Activate();await IslandTransition.FadeInAsync(_memoryPalace);
        }
        catch{_memoryPalace?.Hide();_runtime.EndAssistant();}
        finally{_memoryOpening=false;TryPresentNotification();}
    }
    private MemoryPalaceWindow CreateMemoryPalace()
    {
        var window=new MemoryPalaceWindow(_personalStore);
        window.Closed+=(_,_)=>{_memoryPalace=null;if(!_notificationsExiting)OpenAssistant();};
        return window;
    }
}
