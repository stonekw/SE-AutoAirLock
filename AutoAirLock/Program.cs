using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using VRage;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.Game.ObjectBuilders.Definitions;
using VRageMath;

namespace IngameScript
{
    partial class Program : MyGridProgram
    {
        public MyCommandLine _commandLine = new MyCommandLine();
        MyIni _ini = new MyIni();

        public string outsideDoorName;
        public string insideDoorName;
        public string airVentName;
        public IMyAirVent airVent;
        public IMyDoor outsideDoor;
        public IMyDoor insideDoor;
        public int currentWaitInTicks= 0;
        public int timerWaitInSeconds;
        public int timerWaitInTicks;
        public PressureActions CurrentAction;
        public ExecutionLocations ExecutedFrom;
        public enum PressureActions
        {
            Depressurize,
            Pressurize
        }
        public enum ExecutionLocations
        {
            OutsideDoor,
            InsideDoor,
            Airlock,
            Terminal
        }
        public Dictionary<string, ExecutionLocations> ArgumentExecutionLocationMappings = new Dictionary<string, ExecutionLocations>
        {
            {"outside",ExecutionLocations.OutsideDoor },
            {"inside",ExecutionLocations.InsideDoor },
            {"airlock",ExecutionLocations.Airlock },
            {"terminal",ExecutionLocations.Terminal },
        };
        public Program()
        {
            MyIniParseResult result;
            if (!_ini.TryParse(Me.CustomData, out result))
                throw new Exception(result.ToString());
        }

        public void Save()
        {

        }

        public void Main(string argument, UpdateType updateSource)
        {
            try
            {
                Echo($"_ini.Get(\"general\", \"timerDelayInSeconds\").ToInt32();{_ini.Get("general", "timerDelayInSeconds").ToInt32()}");
                if (_ini.ContainsKey("general", "timerDelayInSeconds"))
                {
                    timerWaitInSeconds = _ini.Get("general", "timerDelayInSeconds").ToInt32();
                } else
                {
                    timerWaitInSeconds = 1;
                }
                timerWaitInTicks = timerWaitInSeconds * 60;

                //if waiting for pressurization/depressurization
                if (updateSource == UpdateType.Update10)
                {

                    //60 ticks per second
                    if (currentWaitInTicks < timerWaitInTicks)
                    {
                        
                        currentWaitInTicks += 10;
                        Echo($"currentWaitInTicks={currentWaitInTicks}");
                        return;
                    } else
                    {

                        currentWaitInTicks = 0;
                        Runtime.UpdateFrequency = UpdateFrequency.None;

                        if (CurrentAction == PressureActions.Depressurize)
                        {
                            depressurizeEnd();
                        } else if (CurrentAction == PressureActions.Pressurize)
                        {
                            pressurizeEnd();
                        }
                    }     
                }
                else
                {

                    bool parsedArguments = _commandLine.TryParse(argument);

                    if (parsedArguments == false)
                    {
                        throw new Exception("Cannot parse arguments.");
                    }


                    outsideDoorName = _commandLine.Argument(0);
                    insideDoorName = _commandLine.Argument(1);
                    airVentName = _commandLine.Argument(2);
                    string executionLocationArg = _commandLine.Argument(3);

                    bool missingRequiredParams = (outsideDoorName == null || insideDoorName == null || airVentName == null || executionLocationArg == null);

                    if (missingRequiredParams)
                    {
                        throw new Exception($"Must provide all required params: \"outsideDoorName\" \"insideDoorName\" \"airVentName\" \"executionLocation\"");
                    }

                    bool locationMapped = ArgumentExecutionLocationMappings.TryGetValue(executionLocationArg, out ExecutedFrom);

                    if (!locationMapped)
                    {
                        throw new Exception($"Must provide valid location executing from (argument 4): \"{executionLocationArg}\" was provided, must provide 'outside' 'inside' 'airlock' or 'terminal'.");
                    }

                    bool pressurize = _commandLine.Switch("pressurize");
                    bool depressurize = _commandLine.Switch("depressurize");
                    bool switchError = (pressurize && depressurize || !pressurize && !depressurize) && ExecutedFrom == ExecutionLocations.Terminal;

                    if (switchError)
                    {
                        throw new Exception($"Cannot run airlock script. Must pressurize or depressurize if executing from terminal.");
                    }

                    if (outsideDoor == null || outsideDoor.Name != outsideDoorName)
                    {
                        outsideDoor = GridTerminalSystem.GetBlockWithName(outsideDoorName) as IMyDoor;
                        if (outsideDoor == null)
                        {
                            throw new Exception($"Outside door {outsideDoorName} does not exist or is not a door.");
                        }
                    }
                    if (insideDoor == null || insideDoor.Name != insideDoorName)
                    {
                        insideDoor = GridTerminalSystem.GetBlockWithName(insideDoorName) as IMyDoor;
                        if (insideDoor == null)
                        {
                            throw new Exception($"Inside door {insideDoorName} does not exist or is not a door.");
                        }
                    }

                    if (airVent == null || airVent.Name != airVentName)
                    {
                        airVent = GridTerminalSystem.GetBlockWithName(airVentName) as IMyAirVent;
                        if (airVent == null)
                        {
                            throw new Exception($"Air vent {airVentName} does not exist or is not an air vent.");
                        }
                    }

                    bool isAirtight = airVent.CanPressurize;

                    if (isAirtight && (ExecutedFrom == ExecutionLocations.OutsideDoor || ExecutedFrom == ExecutionLocations.Airlock))
                    {
                        depressurizeStart();
                    }
                    if (isAirtight && ExecutedFrom == ExecutionLocations.InsideDoor)
                    {
                        insideDoor.OpenDoor();
                    }
                    if (!isAirtight && ExecutedFrom == ExecutionLocations.OutsideDoor)
                    {
                        outsideDoor.OpenDoor();
                    }
                    if (!isAirtight && (ExecutedFrom == ExecutionLocations.InsideDoor || ExecutedFrom == ExecutionLocations.Airlock))
                    {
                        pressurizeStart();
                    }
                    if (ExecutedFrom == ExecutionLocations.Terminal && pressurize)
                    {
                        pressurizeStart();

                    }
                    else if (ExecutedFrom == ExecutionLocations.Terminal && depressurize)
                    {
                        depressurizeStart();
                    }
                }
            } catch (Exception e)
            {
                Echo($"Unexepcted error:{Environment.NewLine}{e.Message}{Environment.NewLine}{e.StackTrace}");
            }

        }
        public void pressurizeStart()
        {
            CurrentAction = PressureActions.Pressurize;
            outsideDoor.CloseDoor();
            //outsideDoor.Enabled = false;
            insideDoor.CloseDoor();
            //insideDoor.Enabled = false;
            airVent.Depressurize = false;
            Runtime.UpdateFrequency = UpdateFrequency.Update10;

        }
        public void depressurizeStart()
        {
            CurrentAction = PressureActions.Depressurize;
            outsideDoor.CloseDoor();
            //outsideDoor.Enabled = false;
            insideDoor.CloseDoor();
            //insideDoor.Enabled = false;
            airVent.Depressurize = true;
            Runtime.UpdateFrequency = UpdateFrequency.Update10;
        }
        public void pressurizeEnd()
        {
            insideDoor.Enabled = true;
            insideDoor.OpenDoor();
        }
        public void depressurizeEnd()
        {
            outsideDoor.Enabled = true;
            outsideDoor.OpenDoor();
        }
    }
}
