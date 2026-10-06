using System.Globalization;

namespace EndfieldChargePlus.Views;

/// <summary>Presentation cursor over complete text; never splits an emoji or combining character.</summary>
internal sealed class ReplyTextReveal
{
    private readonly string _text;
    private readonly int[] _boundaries;
    private readonly double _rate;
    private int _shown;
    private double _credit;

    internal ReplyTextReveal(string text)
    {
        _text=text;
        _boundaries=StringInfo.ParseCombiningCharacters(text);
        _shown=Math.Min(1,_boundaries.Length);
        _rate=Math.Max(140,_boundaries.Length/8d);
    }
    internal bool IsComplete=>_shown==_boundaries.Length;
    internal string VisibleText=>IsComplete?_text:_text[.._boundaries[_shown]];
    internal bool Advance(TimeSpan elapsed)
    {
        if(IsComplete)return false;
        // A stalled UI frame must not reveal an entire paragraph in one catch-up burst.
        _credit+=Math.Clamp(elapsed.TotalSeconds,0,.08)*_rate;
        var count=(int)_credit;
        if(count==0)return false;
        _credit-=count;
        _shown=Math.Min(_boundaries.Length,_shown+count);
        return true;
    }
}
