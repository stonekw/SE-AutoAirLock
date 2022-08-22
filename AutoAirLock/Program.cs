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
        public List<IMyAirVent> airVents = new List<IMyAirVent>();
        public List<IMyDoor> outsideDoors = new List<IMyDoor>();
        public List<IMyDoor> insideDoors = new List<IMyDoor>();
        public List<IMyProgrammableBlock> linkedAirlockBlocks = new List<IMyProgrammableBlock>();
        public int currentWaitInTicks= 0;
        public decimal timerWaitInSeconds;
        public decimal timerWaitInTicks;
        public PressureActions CurrentAction;
        public ExecutionLocations ExecutedFrom;
        public AirlockMode? Mode;
        bool noLink = false;//no link if true will not call linked airlocks
        public Dictionary<string, AirlockMode> iniStringModeEnumMapping = new Dictionary<string, AirlockMode>
        {
            {"standard", AirlockMode.Standard },
            {"hangar", AirlockMode.Hangar}
        };
        public enum AirlockMode
        {
            Standard,
            Hangar
        }
        public enum PressureActions
        {
            Depressurize,
            Pressurize,
            Toggle
        }
        public Dictionary<string, PressureActions> TerminalSwitchActionMapping = new Dictionary<string, PressureActions> 
        {
            {"depressurize",PressureActions.Depressurize},
            {"pressurize",PressureActions.Pressurize},
            {"toggle",PressureActions.Toggle},
        };
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

                if (_ini.ContainsKey("general", "timerDelayInSeconds"))
                {
                    timerWaitInSeconds = _ini.Get("general", "timerDelayInSeconds").ToDecimal();
                } else
                {
                    timerWaitInSeconds = 1;
                }
                timerWaitInTicks = timerWaitInSeconds * 60;

                string availableModes = string.Join("\" ", iniStringModeEnumMapping.Keys);
                if (_ini.ContainsKey("general", "mode"))
                {
                    string stringMode = _ini.Get("general", "mode").ToString();
                    bool hasKey = iniStringModeEnumMapping.ContainsKey(stringMode);
                    if (!hasKey)
                    {
                        throw new Exception($"Invalid mode provided \"{stringMode}\". Available options: \"{availableModes}\".");
                    }
                    else
                    {
                        Mode = iniStringModeEnumMapping[stringMode];
                    }
                }
                else
                {
                    throw new Exception($"No mode provided in Custom Data. Available options: \"{availableModes}\". Default: \"standard\"");

                }
                if (Mode == AirlockMode.Hangar)
                {
                    string hangarDoorGroupName = _ini.Get("general", "hangarDoorGroupName").ToString();
                    if (string.IsNullOrEmpty(hangarDoorGroupName))
                    {
                        throw new Exception($"For hangar mode, hangarDoorGroupName config value required. Group all air-tight hangar doors as a block group.");
                    }
                    IMyBlockGroup hangarDoorGroup = GridTerminalSystem.GetBlockGroupWithName(hangarDoorGroupName);
                    if (hangarDoorGroup == null)
                    {
                        throw new Exception($"Cannot find block group with name \"{hangarDoorGroupName}\"");
                    }
                    //IMyHangarDoor implements IMyDoor
                    hangarDoorGroup.GetBlocksOfType<IMyDoor>(outsideDoors);
                    if (outsideDoors.Count == 0)
                    {
                        throw new Exception($"Hangar door group \"{hangarDoorGroupName}\" does not contain any doors.");
                    }
                }
                if (Mode == AirlockMode.Standard)
                {
                    outsideDoorName = _ini.Get("general", "outsideDoorName").ToString();
                    insideDoorName = _ini.Get("general", "insideDoorName").ToString();
                    if (string.IsNullOrEmpty(outsideDoorName) || string.IsNullOrEmpty(insideDoorName))
                    {
                        throw new Exception("Must provide outsideDoorName and insideDoorName in custom data. If mutliple doors, separate with comma.");
                    }
                    if (outsideDoorName.Contains(","))
                    {
                        List<string> outsideDoorNameList = outsideDoorName.Split(',').ToList();
                        for (var i = 0; i < outsideDoorNameList.Count(); i++)
                        {
                            IMyTerminalBlock block = GridTerminalSystem.GetBlockWithName(outsideDoorNameList[i]);
                            if (block == null)
                            {
                                throw new Exception($"Could not find door  \"{outsideDoorNameList[i]}\" on this grid.");
                            }
                            if (!(block is IMyDoor))
                            {
                                throw new Exception($"Block \"{outsideDoorNameList[i]}\" does not implement a door. It is type \"{block.GetType()}\"");
                            }
                            outsideDoors.Add(block as IMyDoor);
                        }
                    } else
                    {
                        IMyTerminalBlock block = GridTerminalSystem.GetBlockWithName(outsideDoorName);
                        if (block == null)
                        {
                            throw new Exception($"Could not find door  \"{outsideDoorName}\" on this grid.");
                        }
                        if (!(block is IMyDoor))
                        {
                            throw new Exception($"Block \"{outsideDoorName}\" does not implement a door or could not be found.");
                        }
                        outsideDoors.Add(block as IMyDoor);
                    }
                    if (insideDoorName.Contains(","))
                    {
                        List<string> insideDoorNameList = insideDoorName.Split(',').ToList();
                        for (var i = 0; i < insideDoorNameList.Count(); i++)
                        {
                            IMyTerminalBlock block = GridTerminalSystem.GetBlockWithName(insideDoorNameList[i]);
                            if (block == null)
                            {
                                throw new Exception($"Could not find door  \"{insideDoorNameList[i]}\" on this grid.");
                            }
                            if (!(block is IMyDoor))
                            {
                                throw new Exception($"Block \"{insideDoorNameList[i]}\" does not implement a door. It is type \"{block.GetType()}\"");
                            }
                            insideDoors.Add(block as IMyDoor);
                        }
                    }
                    else
                    {
                        IMyTerminalBlock block = GridTerminalSystem.GetBlockWithName(insideDoorName);
                        if (block == null)
                        {
                            throw new Exception($"Could not find door  \"{insideDoorName}\" on this grid.");
                        }
                        if (!(block is IMyDoor))
                        {
                            throw new Exception($"Block \"{insideDoorName}\" does not implement a door. It is type \"{block.GetType()}\"");
                        }
                        insideDoors.Add(block as IMyDoor);
                    }
                    if (insideDoors.Count == 0 || outsideDoors.Count == 0)
                    {
                        throw new Exception($"Must have at least one inside door and one outside door. Inside door count: {insideDoors.Count} Outside door count: {outsideDoors.Count}");
                    }

                }
                //linked airlocks will keep eachother in same pressurization status
                string linkedAirlockProgrammableBlockName = _ini.Get("general", "linkedAirlockProgrammableBlockName").ToString();
                if (string.IsNullOrEmpty(linkedAirlockProgrammableBlockName) && Mode == AirlockMode.Hangar)
                {
                    Echo($"For hangar mode, linkedAirlockProgrammableBlockName is helpful to avoid infinite loop if hangar is connected to inside, pressurized strucutre via an air lock. Param is optional. Allows multiple, separated by comma.");
                }
                if (!string.IsNullOrEmpty(linkedAirlockProgrammableBlockName))
                {
                    if (linkedAirlockProgrammableBlockName.Contains(","))
                    {
                        List<string> linkedAirlockProgrammableBlockNameList = linkedAirlockProgrammableBlockName.Split(',').ToList();
                        for (var i = 0; i < linkedAirlockProgrammableBlockNameList.Count(); i++)
                        {
                            IMyTerminalBlock block = GridTerminalSystem.GetBlockWithName(linkedAirlockProgrammableBlockNameList[i]);
                            if (block == null)
                            {
                                throw new Exception($"Could not find programmable block  \"{linkedAirlockProgrammableBlockNameList[i]}\" on this grid.");
                            }
                            if (!(block is IMyProgrammableBlock))
                            {
                                throw new Exception($"Block \"{linkedAirlockProgrammableBlockNameList[i]}\" does not implement a programmable block. It is type \"{block.GetType()}\"");
                            }
                            linkedAirlockBlocks.Add(block as IMyProgrammableBlock);
                        }
                    }
                    else
                    {
                        IMyTerminalBlock block = GridTerminalSystem.GetBlockWithName(linkedAirlockProgrammableBlockName);
                        if (block == null)
                        {
                            throw new Exception($"Could not find programmable block \"{linkedAirlockProgrammableBlockName}\" on this grid.");
                        }
                        if (!(block is IMyProgrammableBlock))
                        {
                            throw new Exception($"Block \"{linkedAirlockProgrammableBlockName}\" does not implement a programmable block or could not be found.");
                        }
                        linkedAirlockBlocks.Add(block as IMyProgrammableBlock);
                    }
                }

                airVentName = _ini.Get("general", "airVentName").ToString();
                if (string.IsNullOrEmpty(airVentName))
                {
                    throw new Exception("Must provide airVentName in custom data. If mutliple air vents, separate with comma.");
                }
                if (airVentName.Contains(","))
                {
                    List<string> airVentNameNameList = airVentName.Split(',').ToList();
                    for (var i = 0; i < airVentNameNameList.Count(); i++)
                    {
                        IMyTerminalBlock block = GridTerminalSystem.GetBlockWithName(airVentNameNameList[i]);
                        if (block == null)
                        {
                            throw new Exception($"Could not find air vent \"{airVentNameNameList[i]}\" on this grid.");
                        }
                        if (!(block is IMyAirVent))
                        {
                            throw new Exception($"Block \"{airVentNameNameList[i]}\" does not implement an airvent. It is type \"{block.GetType()}\"");
                        }
                        airVents.Add(block as IMyAirVent);
                    }
                }
                else
                {
                    IMyTerminalBlock block = GridTerminalSystem.GetBlockWithName(airVentName);
                    if (block == null)
                    {
                        throw new Exception($"Could not find air vent \"{airVentName}\" on this grid.");
                    }
                    if (!(block is IMyAirVent))
                    {
                        throw new Exception($"Block \"{airVentName}\" does not implement a vent. It is type \"{block.GetType()}\"");
                    }
                    airVents.Add(block as IMyAirVent);
                }
                if (airVents.Count == 0)
                {
                    throw new Exception($"Must have at least one airvent. Air vent count: {airVents.Count}");
                }

                //if waiting for pressurization/depressurization
                if (updateSource == UpdateType.Update10)
                {

                    //60 ticks per second
                    if (currentWaitInTicks < timerWaitInTicks)
                    {
                        
                        currentWaitInTicks += 10;
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
                        throw new Exception("Cannot parse arguments. Must provide at least one argument.");
                    }

                    string executionLocationArg = _commandLine.Argument(0);
                    string possibleLocations = string.Join("\" ", ArgumentExecutionLocationMappings.Keys);

                    
                    if (executionLocationArg == null)
                    {
                        
                        throw new Exception($"Must provide all required params: \"executionLocation\". Possible options: \"{possibleLocations}\"");
                    }

                    bool locationMapped = ArgumentExecutionLocationMappings.TryGetValue(executionLocationArg, out ExecutedFrom);

                    if (!locationMapped)
                    {
                        throw new Exception($"Must provide valid location executing from (argument 4): \"{executionLocationArg}\" was provided, must provide \"{possibleLocations}\".");
                    }

                    bool pressurize = _commandLine.Switch(TerminalSwitchActionMapping.FirstOrDefault(kvp => kvp.Value == PressureActions.Pressurize).Key);
                    bool depressurize = _commandLine.Switch(TerminalSwitchActionMapping.FirstOrDefault(kvp => kvp.Value == PressureActions.Depressurize).Key);
                    bool toggle = _commandLine.Switch(TerminalSwitchActionMapping.FirstOrDefault(kvp => kvp.Value == PressureActions.Toggle).Key);
                    bool switchError = ((pressurize && depressurize || pressurize && toggle || depressurize && toggle) || !pressurize && !depressurize && !toggle) && ExecutedFrom == ExecutionLocations.Terminal;

                    noLink = _commandLine.Switch("nolink");

                    if (switchError)
                    {
                        throw new Exception($"Cannot run airlock script. Must pressurize or depressurize or toggle if executing from terminal. Cannot combine switches");
                    }

                    bool isAirtight = airVents.All(a => a.CanPressurize);

                    if (Mode == AirlockMode.Standard)
                    {
                        if (isAirtight && (ExecutedFrom == ExecutionLocations.OutsideDoor || ExecutedFrom == ExecutionLocations.Airlock))
                        {
                            depressurizeStart();
                        }
                        if (isAirtight && ExecutedFrom == ExecutionLocations.InsideDoor)
                        {
                            insideDoors.ForEach(d => d.OpenDoor());
                        }
                        if (!isAirtight && ExecutedFrom == ExecutionLocations.OutsideDoor)
                        {
                            outsideDoors.ForEach(d => d.OpenDoor());
                        }
                        if (!isAirtight && (ExecutedFrom == ExecutionLocations.InsideDoor || ExecutedFrom == ExecutionLocations.Airlock))
                        {
                            pressurizeStart();
                        }
                    } else if (Mode == AirlockMode.Hangar)
                    {
                        if (isAirtight)
                        {
                            depressurizeStart();
                        }
                        if (!isAirtight)
                        {
                            pressurizeStart();
                        }  
                    }
                   
                    if (ExecutedFrom == ExecutionLocations.Terminal && pressurize)
                    {
                        pressurizeStart();

                    }
                    else if (ExecutedFrom == ExecutionLocations.Terminal && depressurize)
                    {
                        depressurizeStart();
                    }
                    else if (ExecutedFrom == ExecutionLocations.Terminal && toggle)
                    {
                        if (isAirtight)
                        {
                            depressurizeStart();
                        } else
                        {
                            pressurizeStart();
                        }
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
            outsideDoors.ForEach(d => d.CloseDoor());
            //outsideDoor.Enabled = false;
            insideDoors.ForEach(d => d.CloseDoor());
            //insideDoor.Enabled = false;
            airVents.ForEach(a => a.Depressurize = false);
            Runtime.UpdateFrequency = UpdateFrequency.Update10;
            if (!noLink)
            {
                foreach (IMyProgrammableBlock programmableBlock in linkedAirlockBlocks)
                {
                    //do opposite action for linked airlocks
                    programmableBlock.ApplyAction("Run", new List<TerminalActionParameter> { TerminalActionParameter.Get("\"terminal\" -pressurize -nolink") });
                }
            }

        }
        public void depressurizeStart()
        {
            CurrentAction = PressureActions.Depressurize;
            outsideDoors.ForEach(d => d.CloseDoor());
            //outsideDoor.Enabled = false;
            insideDoors.ForEach(d => d.CloseDoor());
            //insideDoor.Enabled = false;
            airVents.ForEach(a => a.Depressurize = true);
            Runtime.UpdateFrequency = UpdateFrequency.Update10;
            if (!noLink)
            {
                foreach (IMyProgrammableBlock programmableBlock in linkedAirlockBlocks)
                {
                    //do opposite action for linked airlocks
                    programmableBlock.ApplyAction("Run", new List<TerminalActionParameter> { TerminalActionParameter.Get("\"terminal\" -depressurize -nolink") });
                }
            }

        }
        public void pressurizeEnd()
        {
            if (Mode == AirlockMode.Standard)
            {
                insideDoors.ForEach(d => d.Enabled = true);
                insideDoors.ForEach(d => d.OpenDoor());
            } else if (Mode == AirlockMode.Hangar)
            {
                outsideDoors.ForEach((d) => d.CloseDoor());
            }
        }
        public void depressurizeEnd()
        {
            outsideDoors.ForEach(d => d.Enabled = true);
            outsideDoors.ForEach(d => d.OpenDoor());
        }
    }
}
