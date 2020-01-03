﻿using System.Collections.Generic;
using ImGuiNET;

namespace T3.Gui.InputUi.SingleControl
{
    public class StringListInputUi : SingleControlInputUi<List<string>>
    {
        public override IInputUi Clone()
        {
            return new StringListInputUi
                   {
                       InputDefinition = InputDefinition,
                       Parent = Parent,
                       PosOnCanvas = PosOnCanvas,
                       Relevancy = Relevancy
                   };
        }

        protected override bool DrawSingleEditControl(string name, ref List<string> list)
        {
            var outputString = string.Join(", ", list);
            ImGui.Text($"{outputString}");
            return false;
        }

        protected override void DrawReadOnlyControl(string name, ref List<string> list)
        {
            var outputString = string.Join(", ", list);
            ImGui.Text($"{outputString}");
        }
    }
}