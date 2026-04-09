# [PART 1] SYSTEM_PROMPT
# ============================================================
# TIA COPILOT - HMI SCREEN GENERATION ENGINE
# Target Platform: Siemens WinCC Unified (TIA Portal V18+)
# ============================================================

You are an expert Siemens WinCC Unified HMI screen designer.
Your job is to analyze the user's request and generate a structured LOGICAL JSON description of an HMI screen.

## YOUR ROLE:
- You describe WHAT objects exist on screen, WHICH tags they bind to, and WHAT behaviors they have.
- You DO NOT generate pixel coordinates, LibraryPaths, or JavaScript script strings — those are handled by the C# assembler.
- You DO NOT invent tag names. You MUST bind objects only to tags provided in the [AVAILABLE TAGS] section.
- You MUST strictly follow the provided JSON output schema. Do not add or remove keys.

## PLATFORM CONTEXT:
- Target runtime: WinCC Unified PC Runtime
- Script language inside the runtime: JavaScript (HMIRuntime API)
- All tag reads in scripts use: Tags('TagName').Read()
- All tag writes in scripts use: Tags('TagName').Write(value)
- Screen navigation uses: HMIRuntime.UI.SysFct.ChangeScreen('ScreenName', null)

## OBJECT CATEGORY OVERVIEW:
- **Library Objects**: Tank, Valve, Motor, Pipe — always backed by IndustryGraphicLibrary
- **Primitive Shapes**: Rectangle, Circle, CircularArc, CircleSegment — simple drawn objects
- **Input/Output Controls**: Button, IOField, Bar, Gauge, Slider, CheckBoxGroup, RadioButtonGroup, TouchArea
- **Data Controls**: TrendControl, AlarmControl, FunctionTrendControl, DetailedParameterControl
- **Diagnostic Controls**: SystemDiagnosisControl
- **Media Controls**: MediaControl, WebControl
- **Container Controls**: ScreenWindow, Clock

## BEHAVIOR RULES:
- A "color_on_status" behavior means the object changes fill/background color based on a boolean tag (TRUE = active color, FALSE = inactive color).
- A "fill_level" behavior means the object displays a visual fill percentage driven by an analog tag.
- Buttons use "keydown_write" and "keyup_write" to produce momentary pulse signals on a tag.
- Navigation buttons use "navigate_to" with a target screen name.

# [PART 2] RAG_CONTEXT
# ============================================================
# CHUNKED KNOWLEDGE BASE — DO NOT EDIT STRUCTURE BELOW THIS LINE
# Each chunk begins with "## STRATEGY:" and is ingested individually into ChromaDB.
# Metadata type assignment in ingest_hmi.py:
#   WIDGET   → Tank, Valve, Motor, Pipe, Pump (library-backed objects)
#   CONTROL  → TrendControl, AlarmControl, SystemDiagnosisControl, DetailedParameterControl (complex controls)
#   SCREEN   → ScreenWindow, navigation, screen-level rules
#   LAYOUT   → general positioning, grouping, and layout guidance
# ============================================================

## STRATEGY: LIBRARY OBJECT — TANK
[PLACEHOLDER — TO BE IMPLEMENTED]
Describe rules for generating Tank objects:
- When to use a Tank vs a Bar
- Subtypes: Barrel, ElementMove,FlatVessel,FlatVessel1,FlatVesselBodyHorizontal,FlatVesselBodyVertical,FlatVesselHead,FlatVesselHead1,FlatVesselHeadHorizontalLeft,FlatVesselHeadHorizontalRight,FlatVesselHopper,Hopper,HopperWindow,HopperWithWindow,
Reactor,Silo,SphericalTank,StorageFacility,StorageFacility1,StorageFacilityHorizontal,Tank
Tank1,Tank2,Tank2WithScale,Tank3,Tank3WithBolts,Tank4,Tank4WithRivets,TankCutaway,TankDoor,TankDoor1,TankHead,TankHorizontal,TankOpening,TankPorthole,TankPorthole1,TankPorthole2.
- Required fields: bind_tag (analog), behaviors list
- Supported behaviors: fill_level, color_on_status
- Naming convention for tank objects
METADATA_TYPE: WIDGET

## STRATEGY: LIBRARY OBJECT — VALVE
[PLACEHOLDER — TO BE IMPLEMENTED]
Describe rules for generating Valve objects:
- Subtypes: ControlValve, HandValve, HandValve1, HandValve2, HandValve2HorizentalFront, HandValvae2Vertical, HandValve2VerticalFront, MiniElectricSafetyShutoffValve, SafetyShutoffValve, ValveOpen, ValveShut
- Required fields: bind_tag (boolean status tag)
- Supported behaviors: color_on_status
- Naming convention
METADATA_TYPE: WIDGET

## STRATEGY: LIBRARY OBJECT — MOTOR / PUMP
[PLACEHOLDER — TO BE IMPLEMENTED]
Describe rules for generating Motor and Pump objects:
- Motor subtypes:Motor,Motor1,Motor2,Motor3,Motor4,Motor5,Motor5Front,Motor6,Motor7,Motor7Front,Motor8,Motor8Front,Motor9Vertical,MotorBase,MotorVentilator,RailClip
- Pump subtypes: 90DegreePump,ClassicPump,CoolPump,DrivePump,EndsuctionCentrifugalPump,ExplosionProofPump,HeavyDutyPlasticCentrifugalPump,HorizontalPumpLeft,HorizontalSplitCasePump,MagneticDrivePump,Pump,Pump1,Pump2,SeallessPump,SelfprimingCentrifugalPump,Ventilator,VerticalPump,VerticalPump1,VerticalPumpDown,VerticalPumpUp.
- Required fields: bind_tag (boolean status tag)
- Supported behaviors: color_on_status
- Naming convention
METADATA_TYPE: WIDGET

## STRATEGY: LIBRARY OBJECT — PIPE
[PLACEHOLDER — TO BE IMPLEMENTED]
Describe rules for Pipe objects:
- PipeHorizontal vs PipeVertical subtypes
- Required fields: bind_tag (boolean flow status tag)
- Supported behaviors: color_on_status
- Naming convention, how pipes connect to tanks and valves
METADATA_TYPE: WIDGET

## STRATEGY: PRIMITIVE SHAPE — RECTANGLE / CIRCLE / ARC / SEGMENT
[PLACEHOLDER — TO BE IMPLEMENTED]
Describe rules for primitive shapes:
- When to use Rectangle vs Circle vs CircularArc vs CircleSegment
- Required fields: bind_tag (boolean)
- Supported behaviors: color_on_status
- Use cases: sensor indicators, status lights
METADATA_TYPE: WIDGET

## STRATEGY: INPUT/OUTPUT — BUTTON
[PLACEHOLDER — TO BE IMPLEMENTED]
Describe rules for Button objects:
- Momentary buttons: keydown_write + keyup_write pattern
- Navigation buttons: navigate_to pattern
- Required fields: label, bind_tag or navigate_to
- Naming convention
METADATA_TYPE: CONTROL

## STRATEGY: INPUT/OUTPUT — IOFIELD / BAR / GAUGE / SLIDER
[PLACEHOLDER — TO BE IMPLEMENTED]
Describe rules for analog display/input controls:
- IOField: displays or edits a tag value with a format string
- Bar: vertical/horizontal fill driven by analog tag with min/max
- Gauge: circular gauge driven by analog tag with min/max
- Slider: write analog value to a tag
- Required fields per type
METADATA_TYPE: CONTROL

## STRATEGY: INPUT/OUTPUT — TOUCHAREA / CHECKBOX / RADIOBUTTON
[PLACEHOLDER — TO BE IMPLEMENTED]
Describe rules for:
- TouchArea: invisible clickable overlay, tooltip, trigger tag
- CheckBoxGroup: boolean toggle display/write
- RadioButtonGroup: enumerated selection write
METADATA_TYPE: CONTROL

## STRATEGY: DATA CONTROL — TRENDCONTROL
[PLACEHOLDER — TO BE IMPLEMENTED]
Describe rules for TrendControl objects:
- Required fields: trend_tag (the tag being logged/displayed), show_ruler
- Differences from FunctionTrendControl
- When to use TrendControl vs Bar/Gauge
METADATA_TYPE: CONTROL

## STRATEGY: DATA CONTROL — ALARMCONTROL
[PLACEHOLDER — TO BE IMPLEMENTED]
Describe rules for AlarmControl objects:
- No tag binding required (system-managed)
- Required fields: none beyond placement
- Use context: always placed on dedicated monitoring screens
METADATA_TYPE: CONTROL

## STRATEGY: DATA CONTROL — DETAILEDPARAMETERCONTROL (RECIPES)
[PLACEHOLDER — TO BE IMPLEMENTED]
Describe rules for DetailedParameterControl:
- Used for recipe/parameter management
- Required fields: parameter_set_id
METADATA_TYPE: CONTROL

## STRATEGY: DIAGNOSTIC CONTROL — SYSTEMDIAGNOSISCONTROL
[PLACEHOLDER — TO BE IMPLEMENTED]
Describe rules for SystemDiagnosisControl:
- No tag binding required (system-managed)
- Use context: always on a dedicated diagnostics screen
METADATA_TYPE: CONTROL

## STRATEGY: MEDIA CONTROL — MEDIACONTROL / WEBCONTROL
[PLACEHOLDER — TO BE IMPLEMENTED]
Describe rules for:
- MediaControl: video playback, requires a local file URL
- WebControl: embedded browser, requires a URL
METADATA_TYPE: CONTROL

## STRATEGY: SCREEN STRUCTURE — SCREENWINDOW / NAVIGATION
[PLACEHOLDER — TO BE IMPLEMENTED]
Describe rules for:
- ScreenWindow: embedding a sub-screen inside a parent screen
- Navigation pattern: Button with navigate_to pointing to a ScreenName
- Screen naming conventions
METADATA_TYPE: SCREEN

## STRATEGY: CLOCK DISPLAY
[PLACEHOLDER — TO BE IMPLEMENTED]
Describe rules for Clock objects:
- No tag binding required
- Required fields: format string, clock_mode (LocalTime / SystemTime)
METADATA_TYPE: LAYOUT

## STRATEGY: INPUT/OUTPUT — HMITOGGLESWITCH
Rules for HmiToggleSwitch objects:
- Use when the user needs a toggle/flip switch that directly writes a boolean value to a PLC tag
- Required fields: bind_tag (BOOL tag the switch controls)
- Optional fields: back_color (default/OFF state background, R,G,B string), alternate_back_color (ON state color, R,G,B string)
- DO NOT include TagColor or Events in the AI output — C# assembler always generates them from bind_tag:
- TagColor = bind_tag value
- Events.OnStateChanged = "Tags("<bind_tag>").Write(item.IsAlternateState);"
- Default back_color if not specified: "242, 244, 255" (light gray-blue, inactive state)
- Default alternate_back_color if not specified: "0, 200, 80" (green, active/ON state)
- Use red alternate color "220, 50, 50" for stop/disable switches
- Naming convention: Switch_<DeviceName> e.g. Switch_Bom_Chinh, Switch_Van_1
- Placed in the left sidebar zone alongside buttons — shares buttonSlot counter
- Behavior: toggle click writes IsAlternateState (true=ON, false=OFF) to the bound tag
METADATA_TYPE: CONTROL

## STRATEGY: COMPOUND CONTROL — DEVICE FACEPLATE
Rules for generating a standard Device Control Faceplate (Motor/Pump/Actuator control group):
- Use cases: Providing a unified control interface for a single process device (e.g., Motor_Main, Pump_Fuel).
- A Faceplate is a conceptual grouping of multiple controls, generated as individual objects by the C# assembler.

### SUB-COMPONENTS & BINDING RULES:
1. *Start Button*: 
   - Pattern: Momentary pulse.
   - Logic: keydown_write = true, keyup_write = false.
   - Target Tag: <DeviceName>_Start (BOOL).
   - Label: "START".
2. *Stop Button*: 
   - Pattern: Momentary pulse.
   - Logic: keydown_write = true, keyup_write = false.
   - Target Tag: <DeviceName>_Stop (BOOL).
   - Label: "STOP". 
   - Visual: Use red background "220, 50, 50" if specified.
3. *Reset Button*: 
   - Pattern: Momentary pulse.
   - Target Tag: <DeviceName>_Reset (BOOL).
   - Label: "RESET".
4. *Mode Switch (Man/Auto)*: 
   - Type: HmiToggleSwitch.
   - Target Tag: <DeviceName>_Mode (BOOL: False = Manual, True = Auto).
   - Label: "MAN / AUTO".
5. *Run Indicator (Running Lamp)*: 
   - Type: Circle or CircleSegment.
   - Behavior: color_on_status.
   - Target Tag: <DeviceName>_Running (BOOL).
   - Active Color: Green "0, 255, 0".
6. *Error Indicator (Fault Lamp)*: 
   - Type: Circle or CircleSegment.
   - Behavior: color_on_status.
   - Target Tag: <DeviceName>_Fault (BOOL).
   - Active Color: Red "255, 0, 0".
7. *Runtime Display*: 
   - Type: IOField.
   - Mode: Read-only.
   - Format: "{F2} h" (for hours) or "{D} m" (for minutes).
   - Target Tag: <DeviceName>_Runtime (Analog).

### LAYOUT CONSTRAINTS:
- Faceplate objects should be grouped vertically in the Control Panel zone (left sidebar).
- Naming Convention: Prefix all sub-objects with FP_<DeviceName>_ (e.g., FP_Motor1_BtnStart).
- Default grouping: Indicators at the top, IOField in the middle, Buttons and Switch at the bottom.

METADATA_TYPE: CONTROL

## STRATEGY: LAYOUT GUIDANCE — GENERAL SCREEN COMPOSITION
Rules for general screen layout (Replaces Sidebar model):
- **Primary Rule**: AI MUST calculate absolute 'Left' and 'Top' coordinates for every object.
- **Cluster Model**: Related objects (e.g., within a Faceplate) must be grouped by proximity.
- **Screen Zones**:
  - Center/Main: For process widgets (Tanks, Pipes).
  - Control Clusters: Grouped Faceplates, usually arranged horizontally.
- **Coordinate Calculation**:
  - Faceplate 1: Start at (100, 100).
  - Faceplate 2: Start at (350, 100) — maintaining 50px gap.
  - Faceplate 3: Start at (600, 100).
- **Naming**: Use 'FP_' prefix for all elements inside a cluster to trigger grouping logic.
METADATA_TYPE: LAYOUT