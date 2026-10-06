using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using EndfieldChargePlus.Assistant;

namespace EndfieldChargePlus.Views;

public partial class AssistantIslandWindow
{
    private readonly DispatcherTimer _replyTimer=new(DispatcherPriority.Background){Interval=TimeSpan.FromMilliseconds(24)};
    private ReplyTextReveal? _replyReveal;
    private ConversationTurn? _revealingTurn;
    private SelectableTextBlock? _revealingText;
    private long _lastReplyFrame;
    private bool _replyScrollPending;

    internal void AppendAndRevealReply(string question,AssistantReply reply)
    {
        FinishReplyReveal();
        // Persist once before animating. Closing, cancelling or preemption cannot truncate history.
        _session.Append(question,reply);
        _revealingTurn=_session.Turns[^1];
        _replyReveal=new ReplyTextReveal(reply.text);
        RenderConversation();QueueLayout();ResumeReplyReveal();
    }
    private void ResumeReplyReveal()
    {
        if(_disposed||_closing||!IsVisible||_replyReveal is null)return;
        if(_replyReveal.IsComplete){FinishReplyReveal();return;}
        _lastReplyFrame=Stopwatch.GetTimestamp();_replyTimer.Start();
    }
    private void OnReplyFrame(object? sender,EventArgs e)
    {
        if(_disposed||_closing||!IsVisible||_replyReveal is null){_replyTimer.Stop();return;}
        var now=Stopwatch.GetTimestamp();var elapsed=Stopwatch.GetElapsedTime(_lastReplyFrame,now);_lastReplyFrame=now;
        if(!_replyReveal.Advance(elapsed))return;
        var follow=ReplyScroll.Extent.Height-ReplyScroll.Viewport.Height-ReplyScroll.Offset.Y<=24;
        if(_revealingText is not null)_revealingText.Text=_replyReveal.VisibleText;
        QueueLayout();
        if(follow&&!_replyScrollPending) {
            _replyScrollPending=true;
            Dispatcher.UIThread.Post(()=>{
                _replyScrollPending=false;
                if(!_disposed&&!_closing&&IsVisible)ReplyScroll.ScrollToEnd();
            },DispatcherPriority.Loaded);
        }
        if(_replyReveal.IsComplete)FinishReplyReveal();
    }
    private void FinishReplyReveal()
    {
        _replyTimer.Stop();
        if(_revealingText is not null&&_revealingTurn is not null)_revealingText.Text=_revealingTurn.Reply.text;
        ResetReplyReveal();
    }
    private void ResetReplyReveal()
    {
        _replyTimer.Stop();_replyReveal=null;_revealingTurn=null;_revealingText=null;
    }
    private void OnReplyVisibilityChanged(object? sender,AvaloniaPropertyChangedEventArgs e)
    {
        if(e.Property!=IsVisibleProperty)return;
        if(IsVisible)ResumeReplyReveal();else _replyTimer.Stop();
    }
}
