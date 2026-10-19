#region License Information (GPL v3)

/*
    ShareX - A program that allows you to take screenshots and share any file type
    Copyright (c) 2007-2026 ShareX Team

    This program is free software; you can redistribute it and/or
    modify it under the terms of the GNU General Public License
    as published by the Free Software Foundation; either version 2
    of the License, or (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program; if not, write to the Free Software
    Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301, USA.

    Optionally you can also view the license at <http://www.gnu.org/licenses/>.
*/

#endregion License Information (GPL v3)

using ShareX.HelpersLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace ShareX
{
    public class ShareXCLIManager : CLIManager
    {
        public ShareXCLIManager(string[] arguments) : base(arguments)
        {
        }

        public bool HasActionCommands
        {
            get
            {
                if (Commands == null || Commands.Count == 0) return false;
                return Commands.Any(IsActionCommand);
            }
        }

        public static bool IsActionCommand(CLICommand command)
        {
            if (command == null) return false;
            if (!command.IsCommand) return true;
            string cmd = command.Command;
            if (string.Equals(cmd, "silent", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(cmd, "s", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(cmd, "nohotkeys", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(cmd, "multi", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(cmd, "m", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(cmd, "autoclose", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(cmd, "sandbox", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(cmd, "portable", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            return true;
        }

        public async Task UseCommandLineArgs()
        {
            await UseCommandLineArgs(Commands);
        }

        public async Task UseCommandLineArgs(List<CLICommand> commands)
        {
            if (commands != null && commands.Count > 0)
            {
                TaskSettings taskSettings = FindCLITask(commands);

                foreach (CLICommand command in commands)
                {
                    DebugHelper.WriteLine("CommandLine: " + command);

                    if (command.IsCommand)
                    {
                        if (CheckCustomUploader(command) || CheckImageEffect(command) || await CheckCLIHotkey(command) || await CheckCLIWorkflow(command) ||
                            await CheckNativeMessagingInput(command))
                        {
                        }

                        continue;
                    }

                    if (URLHelpers.IsValidURL(command.Command))
                    {
                        UploadManager.DownloadAndUploadFile(command.Command, taskSettings);
                    }
                    else
                    {
                        UploadManager.UploadFile(command.Command, taskSettings);
                    }
                }
            }
        }

        private TaskSettings FindCLITask(List<CLICommand> commands)
        {
            if (ApplicationState.HotkeysConfigOrNull is { } hotkeysConfig)
            {
                CLICommand command = commands.FirstOrDefault(x => x.CheckCommand("task") && !string.IsNullOrEmpty(x.Parameter));

                if (command != null)
                {
                    foreach (HotkeySettings hotkeySetting in hotkeysConfig.Hotkeys)
                    {
                        if (command.Parameter == hotkeySetting.TaskSettings.ToString())
                        {
                            return TaskSettings.GetSafeTaskSettings(hotkeySetting.TaskSettings);
                        }
                    }
                }
            }

            return null;
        }

        private bool CheckCustomUploader(CLICommand command)
        {
            if (command.Command.Equals("CustomUploader", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrEmpty(command.Parameter) && command.Parameter.EndsWith(".sxcu", StringComparison.OrdinalIgnoreCase))
                {
                    TaskHelpers.ImportCustomUploader(command.Parameter);
                }

                return true;
            }

            return false;
        }

        private bool CheckImageEffect(CLICommand command)
        {
            if (command.Command.Equals("ImageEffect", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrEmpty(command.Parameter) && command.Parameter.EndsWith(".sxie", StringComparison.OrdinalIgnoreCase))
                {
                    TaskHelpers.ImportImageEffect(command.Parameter);
                }

                return true;
            }

            return false;
        }

        private async Task<bool> CheckCLIHotkey(CLICommand command)
        {
            foreach (HotkeyType job in Helpers.GetEnums<HotkeyType>())
            {
                if (command.CheckCommand(job.ToString()))
                {
                    string filePath = null;

                    try
                    {
                        filePath = CheckParameterForFilePath(command);
                    }
                    catch (Exception e)
                    {
                        DebugHelper.WriteException(e);

                        return true;
                    }

                    await TaskHelpers.ExecuteJob(job, filePath);

                    return true;
                }
            }

            return false;
        }

        private string CheckParameterForFilePath(CLICommand command)
        {
            if (command != null && !string.IsNullOrEmpty(command.Parameter))
            {
                string filePath = FileHelpers.GetAbsolutePath(command.Parameter);

                if (!File.Exists(filePath))
                {
                    throw new FileNotFoundException();
                }

                return filePath;
            }

            return null;
        }

        private async Task<bool> CheckCLIWorkflow(CLICommand command)
        {
            if (ApplicationState.HotkeysConfigOrNull is { } hotkeysConfig && command.CheckCommand("workflow") && !string.IsNullOrEmpty(command.Parameter))
            {
                foreach (HotkeySettings hotkeySetting in hotkeysConfig.Hotkeys)
                {
                    if (hotkeySetting.TaskSettings.Job != HotkeyType.None)
                    {
                        if (command.Parameter == hotkeySetting.TaskSettings.ToString())
                        {
                            await TaskHelpers.ExecuteJob(hotkeySetting.TaskSettings);

                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private async Task<bool> CheckNativeMessagingInput(CLICommand command)
        {
            if (command.Command.Equals("NativeMessagingInput", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrEmpty(command.Parameter) && command.Parameter.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    await TaskHelpers.HandleNativeMessagingInput(command.Parameter);
                }

                return true;
            }

            return false;
        }

        protected override bool CommandAcceptsParameter(string command)
        {
            if (string.IsNullOrEmpty(command))
            {
                return false;
            }

            if (command.Equals("AutoClose", StringComparison.OrdinalIgnoreCase) ||
                command.Equals("NoHotkeys", StringComparison.OrdinalIgnoreCase) ||
                command.Equals("silent", StringComparison.OrdinalIgnoreCase) ||
                command.Equals("s", StringComparison.OrdinalIgnoreCase) ||
                command.Equals("portable", StringComparison.OrdinalIgnoreCase) ||
                command.Equals("p", StringComparison.OrdinalIgnoreCase) ||
                command.Equals("sandbox", StringComparison.OrdinalIgnoreCase) ||
                command.Equals("multi", StringComparison.OrdinalIgnoreCase) ||
                command.Equals("m", StringComparison.OrdinalIgnoreCase) ||
                command.Equals("exit", StringComparison.OrdinalIgnoreCase) ||
                command.Equals("quit", StringComparison.OrdinalIgnoreCase) ||
                command.Equals("uninstall", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (command.Equals("task", StringComparison.OrdinalIgnoreCase) ||
                command.Equals("workflow", StringComparison.OrdinalIgnoreCase) ||
                command.Equals("CustomUploader", StringComparison.OrdinalIgnoreCase) ||
                command.Equals("ImageEffect", StringComparison.OrdinalIgnoreCase) ||
                command.Equals("NativeMessagingInput", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (Enum.TryParse<HotkeyType>(command, true, out HotkeyType hotkeyType))
            {
                return hotkeyType switch
                {
                    HotkeyType.FileUpload or
                    HotkeyType.FolderUpload or
                    HotkeyType.QRCode or
                    HotkeyType.ImageEditor or
                    HotkeyType.ImageEffects or
                    HotkeyType.ImageViewer or
                    HotkeyType.ImageBeautifier or
                    HotkeyType.VideoConverter or
                    HotkeyType.AnalyzeImage or
                    HotkeyType.Metadata or
                    HotkeyType.StripMetadata or
                    HotkeyType.HashCheck or
                    HotkeyType.OCR or
                    HotkeyType.PinToScreen or
                    HotkeyType.PinToScreenFromFile => true,
                    _ => false
                };
            }

            return false;
        }
    }
}
