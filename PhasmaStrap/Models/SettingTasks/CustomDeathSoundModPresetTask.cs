using PhasmaStrap.Models.SettingTasks.Base;

namespace PhasmaStrap.Models.SettingTasks
{
    public class CustomDeathSoundModPresetTask : StringBaseTask
    {
        public CustomDeathSoundModPresetTask() : base("ModPreset", "CustomDeathSound")
        {
            if (File.Exists(Paths.CustomDeathSound))
                OriginalState = Paths.CustomDeathSound;
        }

        public override void Execute()
        {
            if (!String.IsNullOrEmpty(NewState))
            {
                if (String.Compare(NewState, Paths.CustomDeathSound, StringComparison.InvariantCultureIgnoreCase) != 0 && File.Exists(NewState))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(Paths.CustomDeathSound)!);

                    Filesystem.AssertReadOnly(Paths.CustomDeathSound);
                    File.Copy(NewState, Paths.CustomDeathSound, true);
                }
            }
            else if (File.Exists(Paths.CustomDeathSound))
            {
                Filesystem.AssertReadOnly(Paths.CustomDeathSound);
                File.Delete(Paths.CustomDeathSound);
            }

            OriginalState = NewState;
        }
    }
}
