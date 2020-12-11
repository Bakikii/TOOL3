﻿using T3.Core.Logging;

namespace T3.Gui.Interaction.PresetSystem.InputCommands
{
    public abstract class InputCommand
    {
        public abstract void ExecuteOnce(PresetSystem presetSystem);

        public bool UpdateExecution(PresetSystem presetSystem)
        {
            return true; // complete?
        }

        public bool IsInstant = true;
    }

    public abstract class ButtonsPressCommand : InputCommand
    {
        protected readonly int[] Indices;
        protected ButtonsPressCommand(int[] indices)
        {
            Indices = indices;
        }
    }

    public class SavePresetCommand : ButtonsPressCommand
    {
        public SavePresetCommand(int[] indices) : base(indices)
        {
        }

        public override void ExecuteOnce(PresetSystem presetSystem)
        {
            Log.Debug($"{this}.Execute({string.Join(", ", Indices)})");
            foreach (var index in Indices)
            {
                presetSystem.SavePresetAtIndex(index);
            }
        }
    }

    public class ApplyPresetCommand : ButtonsPressCommand
    {
        public ApplyPresetCommand(int[] indices) : base(indices)
        {

        }

        public override void ExecuteOnce(PresetSystem presetSystem)
        {
            Log.Debug($"{this}.Execute({string.Join(", ", Indices)})");
            if (Indices.Length < 1)
            {
                Log.Error("Cant apply preset without valid indices");
                return;
            }
            presetSystem.ActivatePresetAtIndex(Indices[0]);
        }
    }
    
    public class ActivateGroupCommand : ButtonsPressCommand
    {
        public ActivateGroupCommand(int[] indices) : base(indices)
        {
        }

        public override void ExecuteOnce(PresetSystem presetSystem)
        {
            Log.Debug($"{this}.Execute({string.Join(", ", Indices)})");
            if (Indices.Length == 0)
            {
                Log.Error("Indices are undefined?");
                return;
            }

            presetSystem.ActivateGroupAtIndex(Indices[0]);

        }
    }
}