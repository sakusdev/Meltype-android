#!/usr/bin/env python3
from pathlib import Path

path = Path("src/Meltype.Android/MeltypeInputMethodService.cs")
text = path.read_text(encoding="utf-8")

def replace_once(old: str, new: str) -> None:
    global text
    count = text.count(old)
    if count != 1:
        raise SystemExit(f"expected exactly one match, got {count}: {old[:80]!r}")
    text = text.replace(old, new, 1)

replace_once(
'''    private bool _spaceWasSwiped;\n    private bool _direct;''',
'''    private bool _spaceWasSwiped;\n    private long _lastSpaceInputAtMs;\n    private bool _direct;''')

replace_once(
'''    public override void OnFinishInputView(bool finishingInput)\n    {\n        StopBackspaceRepeat();\n        Log.Info(LogTag, $"input-view finish finishingInput={finishingInput}");\n        base.OnFinishInputView(finishingInput);\n    }''',
'''    public override void OnFinishInputView(bool finishingInput)\n    {\n        StopBackspaceRepeat();\n        var sinceSpace = _lastSpaceInputAtMs == 0\n            ? -1\n            : Environment.TickCount64 - _lastSpaceInputAtMs;\n        Log.Info(LogTag,\n            $"input-view finish finishingInput={finishingInput} sinceSpaceMs={sinceSpace}");\n        base.OnFinishInputView(finishingInput);\n    }''')

replace_once(
'''    private void HandleSpace()\n    {\n        if (_spaceWasSwiped)\n        {\n            _spaceWasSwiped = false;\n            return;\n        }\n\n        HandleVirtualKey(VkSpace, ' ', () => CurrentInputConnection?.CommitText(" ", 1));\n    }''',
'''    private void HandleSpace()\n    {\n        if (_spaceWasSwiped)\n        {\n            _spaceWasSwiped = false;\n            return;\n        }\n\n        _lastSpaceInputAtMs = Environment.TickCount64;\n        var connection = CurrentInputConnection;\n        if (connection is null)\n            return;\n\n        var session = _session;\n        var batchStarted = false;\n\n        try\n        {\n            batchStarted = connection.BeginBatchEdit();\n\n            // A plain space outside composition must stay a plain editor edit.\n            // Running it through MeltypeSession used to emit an empty View and then\n            // FinishComposingText(), which some editors interpret as the end of the\n            // active IME interaction and may hide the keyboard.\n            if (session is null || !session.IsComposing || _direct)\n            {\n                connection.CommitText(" ", 1);\n                _hasComposingText = false;\n                ShowIdleTopBar();\n            }\n            else\n            {\n                var (before, after) = SurroundingText();\n                var result = session.HandleKey(\n                    VkSpace, ' ', false, false, false, false, before, after);\n\n                // CommitText already resolves Android composing spans when the\n                // controller commits an English word + space. Avoid the extra\n                // FinishComposingText() call specifically on Space.\n                Apply(result, finishComposition: false);\n\n                if (!result.Consumed)\n                    connection.CommitText(" ", 1);\n            }\n        }\n        catch (Exception ex)\n        {\n            Warn("HandleSpace", ex);\n        }\n        finally\n        {\n            if (batchStarted)\n            {\n                try\n                {\n                    connection.EndBatchEdit();\n                }\n                catch (Exception ex)\n                {\n                    Warn("HandleSpace/EndBatchEdit", ex);\n                }\n            }\n        }\n\n        try\n        {\n            RequestShowSelf(ShowFlags.Implicit);\n        }\n        catch (Exception ex)\n        {\n            Warn("HandleSpace/RequestShowSelf", ex);\n        }\n    }''')

replace_once(
'''    private void Apply(SessionResult? result, bool updateUi = true)''',
'''    private void Apply(\n        SessionResult? result,\n        bool updateUi = true,\n        bool finishComposition = true)''')

replace_once(
'''            else\n            {\n                connection.FinishComposingText();\n                _hasComposingText = false;\n\n                if (updateUi)\n                    ShowIdleTopBar();\n            }''',
'''            else\n            {\n                if (finishComposition)\n                    connection.FinishComposingText();\n                _hasComposingText = false;\n\n                if (updateUi)\n                    ShowIdleTopBar();\n            }''')

path.write_text(text, encoding="utf-8")
print("patched", path)
