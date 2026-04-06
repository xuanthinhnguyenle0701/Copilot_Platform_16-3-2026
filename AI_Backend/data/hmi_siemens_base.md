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
- Required fields: bind_tag (analog), behaviors list
- Supported behaviors: fill_level, color_on_status
- Naming convention for tank objects
METADATA_TYPE: WIDGET

## STRATEGY: LIBRARY OBJECT — VALVE
[PLACEHOLDER — TO BE IMPLEMENTED]
Describe rules for generating Valve objects:
- ControlValve vs GateValve subtypes
- Required fields: bind_tag (boolean status tag)
- Supported behaviors: color_on_status
- Naming convention
METADATA_TYPE: WIDGET

## STRATEGY: LIBRARY OBJECT — MOTOR / PUMP
[PLACEHOLDER — TO BE IMPLEMENTED]
Describe rules for generating Motor and Pump objects:
- Motor subtypes (Motor2, Motor9Vertical, etc.)
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

## STRATEGY: LAYOUT GUIDANCE — GENERAL SCREEN COMPOSITION
[PLACEHOLDER — TO BE IMPLEMENTED]
Describe general layout rules:
- Zone model: process area (center), control panel (left sidebar), status bar (top), navigation (bottom)
- How to group related objects conceptually
- Guidelines for screen naming (Main_, Data_, Diag_, etc.)
METADATA_TYPE: LAYOUT
