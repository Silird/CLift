
using System;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using VRageMath;
using VRage.Game;
using Sandbox.ModAPI.Interfaces;
using Sandbox.ModAPI.Ingame;
using Sandbox.Game.EntityComponents;
using VRage.Game.Components;
using VRage.Collections;
using VRage.Game.ObjectBuilders.Definitions;
using VRage.Game.ModAPI.Ingame;
using SpaceEngineers.Game.ModAPI.Ingame;

public sealed class Program : MyGridProgram
{
    // НАЧАЛО СКРИПТА

    string shipName = "CLift";
    string assemblerName = "Main Forge";
    private List<string> compCargoNames = new List<string> {
        "Large Cargo Container Comp 1",
        "Large Cargo Container Comp 2",
        "Large Cargo Container Comp 3"
    };
    private int MAX_ASSEMBLE_ATTEMPTS = 3;

    private List<IMyShipWelder> welders = new List<IMyShipWelder>();
    private List<IMyShipWelder> weldersConnectUp = new List<IMyShipWelder>();
    private List<IMyShipGrinder> grindersConnectUp = new List<IMyShipGrinder>();
    private List<IMyShipWelder> weldersConnectDown = new List<IMyShipWelder>();
    private List<IMyShipGrinder> grindersConnectDown = new List<IMyShipGrinder>();
    private IMyPistonBase pistonConnectUp;
    private IMyPistonBase pistonConnectDown;
    private List<IMyPistonBase> pistons = new List<IMyPistonBase>();
    private List<IMyTextSurface> screens = new List<IMyTextSurface>(); // Экран для отладки
    private List<IMyTextSurface> screensAlarm = new List<IMyTextSurface>(); // Экран тревоги
    private IMyCargoContainer cargo;
    private IMyAssembler assembler;
    private List<IMyCargoContainer> compCargos = new List<IMyCargoContainer>();
    private IMyShipConnector connector;

    float pistonVelocity = 0.4F; // Скорость движения поршней (0 - 5)
    float pistonVelocityConnect = 2F; // Скорость движения поршней стыковки (0 - 5)

    private StringBuilder message;

    private StringBuilder unreadyMessage = new StringBuilder();
    bool ready = true;
    string command;
    bool reverse = false;

    private Dictionary<MyItemType, int> invItemsForPart = new Dictionary<MyItemType, int>();
    private Dictionary<MyDefinitionId, int> blueprintsForPart = new Dictionary<MyDefinitionId, int>();

    private Stage stage = Stage.DETERMINE;

    private enum Stage
    {
        DETERMINE,
        WELD,
        CONNECT_UP,
        DISCONNECT_DOWN,
        PULL_UP,
        CONNECT_DOWN,
        DISCONNECT_UP,
        PULL_COMP,
        CHECK_COMP,
        OFFSET,
        ERROR_ASSEMBLE
    }

    private float prevValue = -1;
    private int prevTransferResult = -1;
    private int assembleAttempts = 0;

    public Program()
    {
        findBlocks();
    }

    public void Main(string args)
    {
        message = new StringBuilder();
        if ((args != "") && (args != null))
        {
            message.Append("Аргумент программы: " + args + "\n");
            command = args;
        }

        message.Append("Команда: \"" + command + "\"\n");
        if (ready)
        {
            switch (command)
            {
                case "start":
                    {
                        start();
                        break;
                    }
                case "stop":
                    {
                        stop();
                        break;
                    }
                case "test":
                    {
                        Runtime.UpdateFrequency = UpdateFrequency.None;
                        startAlarm();
                        break;
                    }
                default:
                    {
                        Runtime.UpdateFrequency = UpdateFrequency.None;
                        message.Append("Неизвестная команда: \"" + command + "\"\n");
                        break;
                    }
            }
        }
        else
        {
            Runtime.UpdateFrequency = UpdateFrequency.None;
            message.Append("Не найдены все блоки для лифта\n");
            message.Append(unreadyMessage.ToString());
        }

        foreach (var screen in screens)
        {
            screen.WriteText(message);
        }
    }

    private void start()
    {
        Runtime.UpdateFrequency = UpdateFrequency.Update100;
        stopAlarm();
        message.Append("Текущая стадия:\n");
        switch(stage)
        {
            case Stage.DETERMINE: // Определение стадии лифта
                {
                    stage = determineStage();
                    break;
                }
            case Stage.PULL_COMP: // Получение всех компонентов для сварки части лифта
                {
                    pullComp();
                    break;
                }
            case Stage.CHECK_COMP: // Проверка, что лифт получил все нужные детали
                {
                    checkComp();
                    break;
                }
            case Stage.WELD: // Подъём и сварка лифта и сварка верхнего коннектора
                {
                    message.Append("Подъём и сварка\n");
                    if (pistons[0].CurrentPosition == pistons[0].HighestPosition)
                    {
                        pistons.ForEach(piston => piston.Velocity = 0);
                        weldersConnectUp.ForEach(welder => welder.Enabled = false);
                        connector.Connect();
                        prevValue = -1;
                        stage = Stage.CONNECT_UP;
                    } else
                    {
                        grindersConnectUp.ForEach(grinder => grinder.Enabled = false);
                        weldersConnectUp.ForEach(welder => welder.Enabled = true);
                        welders.ForEach(welder => welder.Enabled = true);
                        pistons.ForEach(piston => piston.Velocity = pistonVelocity);
                    }
                    break;
                }
            case Stage.CONNECT_UP: // Стыковка верхнего коннектора
                {
                    message.Append("Стыковка верхнего коннектора\n");

                    if (pistonConnectDown.CurrentPosition > 2.8)
                    {
                        stage = Stage.OFFSET;
                    }
                    else
                    {
                        float newPos = pistonConnectUp.CurrentPosition;
                        if (newPos == prevValue)
                        {
                            prevValue = -1;
                            pistonConnectUp.Velocity = 0;
                            stage = Stage.DISCONNECT_DOWN;
                        }
                        else
                        {
                            prevValue = newPos;
                            pistonConnectUp.Velocity = pistonVelocityConnect;
                        }
                    }

                    break;
                }
            case Stage.DISCONNECT_DOWN: // Отстыковка нижнего коннектора
                {
                    message.Append("Отстыковка нижнего коннектора\n");

                    float newPos = pistonConnectDown.CurrentPosition;
                    if (newPos == 0)
                    {
                        grindersConnectDown.ForEach(grinder => grinder.Enabled = false);
                        pistonConnectDown.Velocity = 0;
                        // message.Append("Надо бы переключить на подъём\nНо пока заблокировано для безопасности");
                        stage = Stage.PULL_UP;
                    }
                    else
                    {
                        if (reverse)
                        {
                            Runtime.UpdateFrequency = UpdateFrequency.Update100;
                            reverse = false;
                            pistonConnectDown.Reverse();
                        }
                        else if (newPos == prevValue)
                        {
                            reverse = true;
                            pistonConnectDown.Reverse();
                            Runtime.UpdateFrequency = UpdateFrequency.Update1;
                        }
                        else
                        {
                            prevValue = newPos;
                            weldersConnectDown.ForEach(welder => welder.Enabled = false);
                            grindersConnectDown.ForEach(grinder => grinder.Enabled = true);
                            pistonConnectDown.Velocity = -pistonVelocityConnect;
                        }
                    }
                    break;
                }
            case Stage.PULL_UP: // Подъём нижней части и сварка нижнего коннектора
                {
                    message.Append("Подъём нижней части\n");
                    if (pistons[0].CurrentPosition == pistons[0].LowestPosition)
                    {
                        weldersConnectDown.ForEach(welder => welder.Enabled = false);
                        pistons.ForEach(piston => piston.Velocity = 0);
                        prevValue = -1;
                        stage = Stage.CONNECT_DOWN;
                    }
                    else
                    {
                        grindersConnectDown.ForEach(grinder => grinder.Enabled = false);
                        weldersConnectDown.ForEach(welder => welder.Enabled = true);
                        pistons.ForEach(piston => piston.Velocity = -pistonVelocity);
                    }
                    break;
                }
            case Stage.CONNECT_DOWN: // Стыковка нижнего коннектора
                {
                    if (pistonConnectUp.CurrentPosition > 2.8)
                    {
                        stage = Stage.OFFSET;
                    }
                    else
                    {
                        message.Append("Стыковка нижнего коннектора\n");
                        float newPos = pistonConnectDown.CurrentPosition;
                        if (newPos == prevValue)
                        {
                            prevValue = -1;
                            pistonConnectDown.Velocity = 0;
                            stage = Stage.DISCONNECT_UP;
                        }
                        else
                        {
                            prevValue = newPos;
                            pistonConnectDown.Velocity = pistonVelocityConnect;
                        }
                    }

                    break;
                }
            case Stage.DISCONNECT_UP: // Отстыковка верхнего коннектора
                {
                    message.Append("Отстыковка верхнего коннектора\n");

                    float newPos = pistonConnectUp.CurrentPosition;
                    if (newPos == 0)
                    {
                        grindersConnectUp.ForEach(grinder => grinder.Enabled = false);
                        pistonConnectUp.Velocity = 0;
                        stage = Stage.PULL_COMP;
                    }
                    else
                    {
                        if (reverse)
                        {
                            Runtime.UpdateFrequency = UpdateFrequency.Update100;
                            reverse = false;
                            pistonConnectUp.Reverse();
                        }
                        else if (newPos == prevValue)
                        {
                            Runtime.UpdateFrequency = UpdateFrequency.Update1;
                            reverse = true;
                            pistonConnectUp.Reverse();
                        }
                        else
                        {
                            prevValue = newPos;
                            welders.ForEach(welder => welder.Enabled = false);
                            weldersConnectUp.ForEach(welder => welder.Enabled = false);
                            grindersConnectUp.ForEach(grinder => grinder.Enabled = true);
                            pistonConnectUp.Velocity = -pistonVelocityConnect;
                        }
                    }

                    break;
                }
            case Stage.OFFSET: // Смещение обоих поршней стыковки, если они оба отодвинулись слишком далеко
                {
                    offset();

                    break;
                }
            case Stage.ERROR_ASSEMBLE: // Не удалось скрафтить материалы
                {
                    startAlarm();
                    message.Append("Не удалось получить все материалы\n");
                    message.Append("Либо не хватает материалов\nЛибо кто-то пиздит компоненты\n");
                    Runtime.UpdateFrequency = UpdateFrequency.None;
                    break;
                }
            default:
                {
                    message.Append("Неизвестная стадия: " + stage);
                    Runtime.UpdateFrequency = UpdateFrequency.None;
                    break;
                }
        }
    }

    private Stage determineStage()
    {
        message.Append("Определение фазы лифта\n");
        prevValue = -1;
        prevTransferResult = -1;
        assembleAttempts = 0;

        if (pistons[0].CurrentPosition == 0)
        {
            if (pistonConnectUp.CurrentPosition > (pistonConnectDown.CurrentPosition + 0.2))
            {
                weldersConnectDown.ForEach(welder => welder.Enabled = true);
                return Stage.CONNECT_DOWN;
            } else
            {
                return Stage.DISCONNECT_UP;
            }
        }
        else if (pistons[0].CurrentPosition == pistons[0].HighestPosition)
        {
            if (pistonConnectDown.CurrentPosition > (pistonConnectUp.CurrentPosition + 0.2))
            {
                weldersConnectUp.ForEach(welder => welder.Enabled = true);
                return Stage.CONNECT_UP;
            } else
            {
                return Stage.DISCONNECT_DOWN;
            }
        }
        else
        {
            if ((pistonConnectDown.CurrentPosition <= 0.3) && (pistonConnectUp.CurrentPosition > 0.3))
            {
                return Stage.PULL_UP;
            }
            else if ((pistonConnectUp.CurrentPosition <= 0.3) && (pistonConnectDown.CurrentPosition > 0.3))
            {
                return Stage.PULL_COMP;
            }
            else
            {
                startAlarm();
                message.Append("Неопределённое состояние\nЛифт висит в воздухе?\n");
            }
        }

        return Stage.PULL_UP;
    }

    private void pullComp()
    {
        message.Append("Заполнение лифта компонентами\n");
        connector.Connect();
        welders.ForEach(welder => welder.Enabled = true);
        var inventories = new List<IMyInventory>();
        inventories.Add(cargo.GetInventory());
        welders.ForEach(welder => inventories.Add(welder.GetInventory()));
        weldersConnectDown.ForEach(welder => inventories.Add(welder.GetInventory()));
        weldersConnectUp.ForEach(welder => inventories.Add(welder.GetInventory()));
        grindersConnectDown.ForEach(grinder => inventories.Add(grinder.GetInventory()));
        grindersConnectUp.ForEach(grinder => inventories.Add(grinder.GetInventory()));
        if (!transferFromCLift(inventories))
        {
            startAlarm();
            message.Append("Проблемы с очисткой лифта от ресурсов\n");
        } else
        {
            if (assembleAttempts == 0)
            {
                toAssembler();
            }

            int result = transferToCLift();
            if (result == -1)
            {
                prevTransferResult = -1;
                assembleAttempts = 0;

                //message.Append("Надо прочекать то, что все ресы есть\n");
                connector.Disconnect();
                stage = Stage.CHECK_COMP;
            }
            else
            {
                if (result == prevTransferResult)
                {
                    if (assembleAttempts >= MAX_ASSEMBLE_ATTEMPTS)
                    {
                        prevTransferResult = -1;
                        assembleAttempts = 0;
                        Runtime.UpdateFrequency = UpdateFrequency.Once;
                        stage = Stage.ERROR_ASSEMBLE;
                        command = "start";
                    }
                    else
                    {
                        toAssembler();
                    }
                } else
                {
                    prevTransferResult = result;
                }
                message.Append("Все ресурсы ещё не доставлены\n");
            }
        }
    }

    private void checkComp()
    {
        message.Append("Проверка заполненных компонентов\n");
        var inv = cargo.GetInventory();
        bool result = true;
        foreach (var item in invItemsForPart)
        {
            if (!inv.ContainItems(item.Value, item.Key))
            {
                result = false;
                break;
            }
        }

        if (result)
        {
            message.Append("Компоненты есть, начинаем варить!\n");
            stage = Stage.WELD;
        } else
        {
            message.Append("Не все компоненты на месте, надо бы попробовать их снова набрать\n");
            stage = Stage.PULL_COMP;
        }
    }

    private void offset()
    {
        message.Append("Смещение стыковки\n");
        if ((pistonConnectUp.CurrentPosition > 2.5) || (pistonConnectDown.CurrentPosition > 2.5))
        {
            pistonConnectUp.MinLimit = 2.5F;
            pistonConnectDown.MinLimit = 2.5F;
            pistonConnectUp.Velocity = -pistonVelocity;
            pistonConnectDown.Velocity = -pistonVelocity;
        }
        else
        {
            pistonConnectUp.MinLimit = 0F;
            pistonConnectDown.MinLimit = 0F;
            pistonConnectUp.Velocity = 0;
            pistonConnectDown.Velocity = 0;
            stage = Stage.DETERMINE;
        }
    }

    private bool transferFromCLift(List<IMyInventory> inventories)
    {
        bool result = true;
        foreach (var src in inventories)
        {
            var items = new List<MyInventoryItem>();
            src.GetItems(items);
            for (int i = 0; i < items.Count; i++)
            {
                VRage.MyFixedPoint currentAmount = items[i].Amount;
                for (int j = 0; j < compCargos.Count; j++)
                {
                    var targetInv = compCargos[j].GetInventory();

                    VRage.MyFixedPoint transferAmount;
                    if (getVolume(targetInv) > getVolume(items[i], currentAmount))
                    {
                        transferAmount = currentAmount;
                    }
                    else
                    {
                        transferAmount = VRage.MyFixedPoint.Floor((VRage.MyFixedPoint)((float)getVolume(targetInv) / items[i].Type.GetItemInfo().Volume));
                    }

                    if (src.TransferItemTo(targetInv, items[i], transferAmount))
                    {
                        currentAmount -= transferAmount;
                    }

                    if (currentAmount == 0)
                    {
                        break;
                    }
                }
                if (currentAmount != 0)
                {
                    result = false;
                }
            }
        }

        return result;
    }

    // -1 - хватает ресурсов
    private int transferToCLift()
    {
        bool result = true;
        VRage.MyFixedPoint transferedItems = 0;
        var target = cargo.GetInventory();
        foreach (var item in invItemsForPart)
        {
            VRage.MyFixedPoint needAmount = item.Value;
            foreach (var src in compCargos)
            {
                var srcInv = src.GetInventory();

                VRage.MyFixedPoint transferAmount;
                var invItemTmp = srcInv.FindItem(item.Key);
                if (invItemTmp != null) {
                    var invItem = (MyInventoryItem) invItemTmp;
                    if (invItem.Amount > needAmount)
                    {
                        transferAmount = needAmount;
                    } else
                    {
                        transferAmount = invItem.Amount;
                    }

                    if (srcInv.TransferItemTo(target, invItem, transferAmount))
                    {
                        transferedItems += transferAmount;
                        needAmount -= transferAmount;
                    }

                    if (needAmount == 0)
                    {
                        break;
                    }
                }
            }
            if (needAmount != 0)
            {
                result = false;
                message.Append("Не хватает " + needAmount + " " + item.Key.SubtypeId + "\n");
            }
        }

        if (result)
        {
            return -1;
        } else
        {
            return transferedItems.ToIntSafe();
        }
    }

    private void toAssembler()
    {
        message.Append("Начало крафта компонентов " + ++assembleAttempts + "/" + MAX_ASSEMBLE_ATTEMPTS +"\n");
        foreach (var blueprint in blueprintsForPart)
        {
            Echo(blueprint.Key + "");
            assembler.AddQueueItem(blueprint.Key, (decimal)blueprint.Value);
        }
    }

    private void startAlarm()
    {
        foreach (var screen in screensAlarm)
        {
            screen.ChangeInterval = 1;
            screen.AddImagesToSelection(new List<string>(new string[] { "Danger", "Cross", "No Entry" }), true);
        }
    }

    private void stopAlarm()
    {
        foreach (var screen in screensAlarm)
        {
            screen.ChangeInterval = 0;
            screen.ClearImagesFromSelection();
        }
    }

    private void stop()
    {
        stage = Stage.DETERMINE;
        startAlarm();
        Runtime.UpdateFrequency = UpdateFrequency.None;
        pistons.ForEach(piston => piston.Velocity = 0);
        connector.Connect();
        pistonConnectDown.Velocity = 0;
        pistonConnectUp.Velocity = 0;
        welders.ForEach(welder => welder.Enabled = false);
        weldersConnectUp.ForEach(welder => welder.Enabled = false);
        weldersConnectDown.ForEach(welder => welder.Enabled = false);
        grindersConnectUp.ForEach(grinder => grinder.Enabled = false);
        grindersConnectDown.ForEach(grinder => grinder.Enabled = false);
    }

    private void findBlocks()
    {
        screensAlarm.Add(getBlock<IMyTextSurface>("LCD Panel Alarm 1 " + shipName, scr => {
            scr.ContentType = VRage.Game.GUI.TextPanel.ContentType.TEXT_AND_IMAGE;
            scr.ClearImagesFromSelection();
        }));
        screensAlarm.Add(getBlock<IMyTextSurface>("LCD Panel Alarm 2 " + shipName, scr => {
            scr.ContentType = VRage.Game.GUI.TextPanel.ContentType.TEXT_AND_IMAGE;
            scr.ClearImagesFromSelection();
        }));
        screens.Add(getBlock<IMyTextSurface>("LCD Panel 1 " + shipName, scr => {
            scr.ContentType = VRage.Game.GUI.TextPanel.ContentType.TEXT_AND_IMAGE;
            scr.FontSize = 1f;
        }));
        screens.Add(getBlock<IMyTextSurface>("LCD Panel 2 " + shipName, scr => {
            scr.ContentType = VRage.Game.GUI.TextPanel.ContentType.TEXT_AND_IMAGE;
            scr.FontSize = 1f;
        }));
        screens.Add(getBlock<IMyTextSurface>("LCD Panel 3 " + shipName, scr => {
            scr.ContentType = VRage.Game.GUI.TextPanel.ContentType.TEXT_AND_IMAGE;
            scr.FontSize = 1f;
        }));

        welders.Add(getFunctionalBlock<IMyShipWelder>("Welder 1 " + shipName));
        welders.Add(getFunctionalBlock<IMyShipWelder>("Welder 2 " + shipName));
        welders.Add(getFunctionalBlock<IMyShipWelder>("Welder 3 " + shipName));
        welders.Add(getFunctionalBlock<IMyShipWelder>("Welder 4 " + shipName));
        welders.Add(getFunctionalBlock<IMyShipWelder>("Welder 5 " + shipName));
        welders.Add(getFunctionalBlock<IMyShipWelder>("Welder 6 " + shipName));
        welders.Add(getFunctionalBlock<IMyShipWelder>("Welder 7 " + shipName));
        weldersConnectUp.Add(getFunctionalBlock<IMyShipWelder>("Welder Connect Up 1 " + shipName));
        weldersConnectUp.Add(getFunctionalBlock<IMyShipWelder>("Welder Connect Up 2 " + shipName));
        weldersConnectDown.Add(getFunctionalBlock<IMyShipWelder>("Welder Connect Down 1 " + shipName));
        weldersConnectDown.Add(getFunctionalBlock<IMyShipWelder>("Welder Connect Down 2 " + shipName));

        grindersConnectUp.Add(getFunctionalBlock<IMyShipGrinder>("Grinder Connect Up 1 " + shipName));
        grindersConnectUp.Add(getFunctionalBlock<IMyShipGrinder>("Grinder Connect Up 2 " + shipName));
        grindersConnectDown.Add(getFunctionalBlock<IMyShipGrinder>("Grinder Connect Down 1 " + shipName));
        grindersConnectDown.Add(getFunctionalBlock<IMyShipGrinder>("Grinder Connect Down 2 " + shipName));

        pistons.Add(getPiston("Piston 11 " + shipName));
        pistons.Add(getPiston("Piston 12 " + shipName));
        pistons.Add(getPiston("Piston 21 " + shipName));
        pistons.Add(getPiston("Piston 22 " + shipName));
        pistons.Add(getPiston("Piston 31 " + shipName));
        pistons.Add(getPiston("Piston 32 " + shipName));
        pistons.Add(getPiston("Piston 41 " + shipName));
        pistons.Add(getPiston("Piston 42 " + shipName));
        pistonConnectUp = getPiston("Piston Connect Up " + shipName);
        pistonConnectDown = getPiston("Piston Connect Down " + shipName);

        cargo = getBlock<IMyCargoContainer>("Cargo Part " + shipName);
        assembler = getBlock<IMyAssembler>(assemblerName);
        compCargoNames.ForEach(compCargoName => compCargos.Add(getBlock<IMyCargoContainer>(compCargoName)));
        connector = getBlock<IMyShipConnector>("Connector " + shipName);
        fillInvItemsForPart();
        fillBlueprintsForPart();
    }
    
    private string strBlueprint = "MyObjectBuilder_BlueprintDefinition";
    private string strComponent = "MyObjectBuilder_Component";
    private string strSteelPlate = "SteelPlate";
    private string strComputer = "Computer";
    private string strComputerBP = "ComputerComponent";
    private string strLargeTube = "LargeTube";
    private string strMotor = "Motor";
    private string strMotorBP = "MotorComponent";
    private string strConstruction = "Construction";
    private string strConstructionBP = "ConstructionComponent";
    private string strInteriorPlate = "InteriorPlate";
    private string strSmallTube = "SmallTube";

    private void fillInvItemsForPart()
    {
        invItemsForPart.Add(MyItemType.Parse(strComponent + "/" + strSteelPlate), 1398);
        invItemsForPart.Add(MyItemType.Parse(strComponent + "/" + strComputer), 8);
        invItemsForPart.Add(MyItemType.Parse(strComponent + "/" + strLargeTube), 24);
        invItemsForPart.Add(MyItemType.Parse(strComponent + "/" + strMotor), 188);
        invItemsForPart.Add(MyItemType.Parse(strComponent + "/" + strConstruction), 700);
        invItemsForPart.Add(MyItemType.Parse(strComponent + "/" + strInteriorPlate), 444);
        invItemsForPart.Add(MyItemType.Parse(strComponent + "/" + strSmallTube), 392);

    }
    private void fillBlueprintsForPart()
    {
        blueprintsForPart.Add(MyDefinitionId.Parse(strBlueprint + "/" + strSteelPlate), 1398);
        blueprintsForPart.Add(MyDefinitionId.Parse(strBlueprint + "/" + strComputerBP), 8);
        blueprintsForPart.Add(MyDefinitionId.Parse(strBlueprint + "/" + strLargeTube), 24);
        blueprintsForPart.Add(MyDefinitionId.Parse(strBlueprint + "/" + strMotorBP), 188);
        blueprintsForPart.Add(MyDefinitionId.Parse(strBlueprint + "/" + strConstructionBP), 700);
        blueprintsForPart.Add(MyDefinitionId.Parse(strBlueprint + "/" + strInteriorPlate), 444);
        blueprintsForPart.Add(MyDefinitionId.Parse(strBlueprint + "/" + strSmallTube), 392);
    }

    private VRage.MyFixedPoint getVolume(IMyInventory inv)
    {
        return inv.MaxVolume - inv.CurrentVolume;
    }

    private VRage.MyFixedPoint getVolume(MyInventoryItem item, VRage.MyFixedPoint amount)
    {
        return item.Type.GetItemInfo().Volume * amount;
    }

    private IMyPistonBase getPiston(String name)
    {
        return getBlock<IMyPistonBase>(name, p => p.Velocity = 0);
    }

    private T getFunctionalBlock<T>(String name) where T : IMyFunctionalBlock
    {
        return getBlock<T>(name, fBlock => fBlock.Enabled = false);
    }

    private T getBlock<T>(String name, Action<T> action = null)
    {
        T block = (T)GridTerminalSystem.GetBlockWithName(name);
        if (block != null)
        {
            action?.Invoke(block);
        }
        else
        {
            ready = false;
            string error = name + " не найден\n";
            Echo(error);
            unreadyMessage.Append(error);
        }
        return block;
    }

    public void Save()
    {
    }
    // КОНЕЦ СКРИПТА
}