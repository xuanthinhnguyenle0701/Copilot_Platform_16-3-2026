# SIEMENS TIA COPILOT KNOWLEDGE BASE

# [PART 1] SYSTEM_INSTRUCTION
# (This section is loaded into the System Prompt. DO NOT CHUNK.)

You are a Senior Automation Engineer specializing in Siemens TIA Portal (S7-1200/1500).
Your ultimate goal is to generate 100% valid Siemens SCL syntax.

**CRITICAL SIEMENS SCL RULES (MUST FOLLOW):**
1. **PREFIX `#`:** ALL local variables (VAR, VAR_INPUT, VAR_OUTPUT, VAR_IN_OUT, VAR_TEMP) MUST be prefixed with `#` in the `body_code` logic (e.g., `#q_Motor := #i_Start;`).
2. **GLOBAL DB:** Global variables or DB calls MUST use double quotes (e.g., `"Global_Data".MotorStatus`).
3. **STRINGS:** String literals must use single quotes (e.g., `'Error_01'`).
4. **HUNGARIAN NOTATION:** 
   - Input: `i_`
   - Output: `q_`
   - InOut: `iq_`
   - Static/Instance: `stat_`
   - Temp: `temp_`
   - Constant: `const_`

**OUTPUT FORMAT:**
You MUST output ONLY a valid JSON object matching the requested schema. DO NOT wrap the JSON in Markdown formatting (no ```json). 
The `body_code` field MUST contain the raw Siemens SCL code with `#` prefixes.

# [PART 2] RAG_CONTEXT

## STRATEGY: BLOCK INSTANTIATION (TIMERS & TRIGGERS)
In Siemens SCL, instances are usually declared as Multi-Instances in `VAR` (Static).
- **Naming Convention:** Prefix `inst_` (e.g., `inst_Timer`, `inst_EdgeFlag`).
- **Syntax for Timers (TON, TOF, TP):** ALWAYS include `PT` with `T#` format.
```scl
VAR
   stat_MyTimer : TON;
   stat_MyTrigger : R_TRIG;
END_VAR
BEGIN
   // Call instance with hash (#)
   #stat_MyTimer(IN := #i_Start, PT := T#5s); 
   #stat_MyTrigger(CLK := #i_Sensor);
```
## STRATEGY: TIMERS/COUNTERS IN STATE MACHINES (CASE)
**CRITICAL RULE:** DO NOT call the same Timer (TON/TOF) or Counter (CTU/CTD) instance multiple times inside different CASE steps or IF branches. 
You MUST call the instance EXACTLY ONCE outside the CASE statement. Control its execution by assigning conditional logic to its inputs (e.g., `IN := (#stat_Step = 3);`).

## STRATEGY: PROGRAM CONTROL STRUCTURES (IF, CASE, FOR, WHILE)
Follow these standard Siemens SCL structures strictly. Always use the `#` prefix for local variables.

```scl
// IF / ELSIF / ELSE statements
IF #i_StartCondition THEN
    // Statement section IF
    #q_MotorRunning := TRUE;
ELSIF #i_StopCondition THEN
    // Statement section ELSIF
    #q_MotorRunning := FALSE;
ELSE
    // Statement section ELSE
    #q_MotorRunning := FALSE;
END_IF;

// CASE statement (CRITICAL for State Machines)
CASE #stat_SequenceStep OF
    1:  // Statement section case 1
        #q_ValveOpen := TRUE;
        #stat_SequenceStep := 2;
    2..4:  // Statement section case 2 to 4
        #q_ValveOpen := FALSE;
    ELSE  // Statement section ELSE
        #q_ValveOpen := FALSE;
        #stat_SequenceStep := 0;
END_CASE;

// FOR loops
FOR #temp_Index := 0 TO 10 DO
    // Statement section FOR
    #stat_DataArray[#temp_Index] := 0;
END_FOR;

// FOR loop with custom step (BY)
FOR #temp_Index := 0 TO 10 BY 2 DO
    // Statement section FOR
    #stat_DataArray[#temp_Index] := 1;
END_FOR;

// WHILE & REPEAT loops
WHILE #temp_IsBusy DO
    // Statement section WHILE
    #temp_WaitCount := #temp_WaitCount + 1;
END_WHILE;

REPEAT
    // Statement section REPEAT
    #temp_ProcessValue := #temp_ProcessValue + 1;
UNTIL #temp_ProcessValue >= 100 END_REPEAT;
```

## STRATEGY: FLOW CONTROL AND CODE STRUCTURING (REGION, CONTINUE, EXIT, GOTO)
Use these statements to control the execution flow and organize the code. 

```scl
// REGION: Used to organize code into collapsible sections in TIA Portal. 
// CRITICAL: DO NOT put comments on the same line as the REGION declaration.
REGION Initialization_Sequence
    #temp_InitDone := TRUE;
    // Statement section REGION
END_REGION

// CONTINUE and EXIT (Used inside loops like FOR or WHILE)
FOR #temp_Index := 0 TO 10 DO
    IF #stat_Array[#temp_Index] = 0 THEN
        CONTINUE; // Skip the rest of this iteration, jump to the next index
    END_IF;
    
    IF #stat_Array[#temp_Index] = 99 THEN
        EXIT; // Break out of the loop completely
    END_IF;
END_FOR;

// GOTO and Labels (Use for explicit jumps - use sparingly)
IF #i_EmergencyStop THEN
    GOTO Error_Handler;
END_IF;

// Normal execution logic
#q_MotorRunning := TRUE;

Error_Handler: // Label declaration
#q_MotorRunning := FALSE;
```

## STRATEGY: TIMER OPERATIONS (TON, TOF, TP, TONR)
Use these standard instantiations for IEC Timers in TIA Portal SCL. 
Critical Rule: Timer instances MUST be declared in `VAR` as Multi-instances. ALWAYS include the `PT` parameter with the `T#...` format (e.g., `T#5s`). Always use the `#` prefix.

```scl
// TP (Pulse), TON (On-Delay), TOF (Off-Delay)
#stat_DelayTimer(IN := #i_StartSignal,
                 PT := T#5s,
                 Q => #q_TimerDone,
                 ET => #stat_ElapsedTime);

// TONR (Retentive On-Delay Timer)
#stat_RetentiveTimer(IN := #i_StartSignal,
                     R := #i_ResetTimer,
                     PT := T#10s,
                     Q => #q_TimerDone,
                     ET => #stat_ElapsedTime);

// Timer Utilities
RESET_TIMER(TIMER := #stat_RetentiveTimer);
PRESET_TIMER(TIMER := #stat_RetentiveTimer, 
             PT := T#15s);
```

## STRATEGY: BIT LOGIC OPERATIONS (R_TRIG, F_TRIG)
Use these specific Siemens system functions for edge detection.
Critical Rule: Edge trigger instances MUST be declared in `VAR` as Multi-instances. DO NOT use `SR` or `RS` blocks for latching in SCL; use Boolean algebra or IF/ELSE instead.

```scl
// R_TRIG (Rising Edge Detection)
#stat_RisingEdge(CLK := #i_InputSignal,
                 Q => #temp_PulseStart);

// F_TRIG (Falling Edge Detection)
#stat_FallingEdge(CLK := #i_InputSignal,
                  Q => #temp_PulseStop);

// Example of Safe Latching (Instead of SR/RS block)
#stat_MotorRunning := #temp_PulseStart OR (#stat_MotorRunning AND NOT #temp_PulseStop);
```

## STRATEGY: COUNTER OPERATIONS (CTU, CTD, CTUD)
Use these standard instantiations for IEC Counters in TIA Portal SCL. 
Critical Rule: Counter instances MUST be declared in `VAR` as Multi-instances (e.g., `stat_CounterUp : CTU`). Always use the `#` prefix for parameters.

```scl
// CTU (Count Up)
// Increments CV when CU transitions from 0 to 1. Sets Q when CV >= PV.
#stat_CounterUp(CU := #i_CountUpPulse,
                R := #i_Reset,
                PV := #i_PresetValue,
                Q => #q_UpperLimitReached,
                CV => #stat_CurrentValue);

// CTD (Count Down)
// Decrements CV when CD transitions from 0 to 1. Sets Q when CV <= 0.
#stat_CounterDown(CD := #i_CountDownPulse,
                  LD := #i_LoadPreset,
                  PV := #i_PresetValue,
                  Q => #q_LowerLimitReached,
                  CV => #stat_CurrentValue);

// CTUD (Count Up Down)
// Combines UP and DOWN counting capabilities.
#stat_CounterUpDown(CU := #i_CountUpPulse,
                    CD := #i_CountDownPulse,
                    R := #i_Reset,
                    LD := #i_LoadPreset,
                    PV := #i_PresetValue,
                    QU => #q_UpperLimitReached,
                    QD => #q_LowerLimitReached,
                    CV => #stat_CurrentValue);
```

## STRATEGY: MATH AND CONVERSION OPERATIONS
Always use correct syntax for math, rounding, and scaling operations. Capitalized parameters like `IN1`, `MN`, `VALUE` are required syntax. Always use the `#` prefix for local variables.
**CRITICAL RULE: Math functions and Analog Scaling (NORM_X, SCALE_X, ABS, MIN, MAX, etc.) are built-in functions. DO NOT instantiate or declare them in `VAR`. Call them directly and assign their return value.**
**For NORM_X and SCALE_X, you MUST use the parameter name 'VALUE', DO NOT use 'IN'.**

```scl
// Math Functions
#temp_Real := ABS(#i_RealVal);
#temp_Sint := MIN(IN1:=#i_SintVal1, IN2:=#i_SintVal2);
#temp_Sint := MAX(IN1:=#i_SintVal1, IN2:=#i_SintVal2);
#temp_Sint := LIMIT(MN:=#i_MinLimit, IN:=#i_ActualVal, MX:=#i_MaxLimit);
#temp_Real := SQR(#i_RealVal);
#temp_Real := SQRT(#i_RealVal);
#temp_Real := LN(#i_RealVal);
#temp_Real := EXP(#i_RealVal);
#temp_Real := SIN(#i_RealVal);
#temp_Real := COS(#i_RealVal);
#temp_Real := TAN(#i_RealVal);
#temp_Real := ASIN(#i_RealVal);
#temp_Real := ACOS(#i_RealVal);
#temp_Real := ATAN(#i_RealVal);
#temp_Real := FRAC(#i_RealVal);

// Explicit Conversion
#temp_Int := BOOL_TO_INT(#i_BoolVal);
#temp_Real := INT_TO_REAL(#i_IntVal);
#temp_Int := REAL_TO_INT(#i_RealVal);

// Rounding
#temp_DInt := CEIL(#i_RealVal);
#temp_DInt := ROUND(#i_RealVal);
#temp_DInt := FLOOR(#i_RealVal);
#temp_DInt := TRUNC(#i_RealVal);

// Analog Scaling (NORM_X / SCALE_X)
// Example: Scaling a raw analog input (0-27648) to a physical value (0.0-100.0)
#temp_NormReal := NORM_X(MIN:=0, VALUE:=#i_RawAnalogInt, MAX:=27648);
#q_ScaledReal := SCALE_X(MIN:=0.0, VALUE:=#temp_NormReal, MAX:=100.0);
```

## STRATEGY: MOVE, SERIALIZATION OPERATIONS (Serialize, MOVE_BLK, FILL_BLK)
Use these specific Siemens system functions for data moving, array manipulation, and byte-level serialization. Always use the `#` prefix for local variables.

```scl
// Serialization / Deserialization (Useful for Network Comms)
Deserialize(SRC_ARRAY:=#iq_ByteArray, DEST_VARIABLE=>#iq_VariantData, POS:=#iq_Position);
Serialize(SRC_VARIABLE:=#iq_VariantData, DEST_ARRAY=>#iq_ByteArray, POS:=#iq_Position);

// Endianness Swap
#temp_DWord := SWAP(#i_DWordVal);

// Standard Block Move (Array to Array)
MOVE_BLK(IN:=#i_SourceArray[0],
         COUNT:=#i_ElementCount,
         OUT=>#q_DestArray[0]);

// Variant Block Move (Advanced Memory Move)
MOVE_BLK_VARIANT(SRC:=#iq_VariantSrc, 
                 COUNT:=#i_ElementCountUdint, 
                 SRC_INDEX:=#i_SrcIndex, 
                 DEST_INDEX:=#i_DestIndex, 
                 DEST=>#iq_VariantDest);

// Uninterruptible Block Move
UMOVE_BLK(IN:=#i_SourceArray[0],
          COUNT:=#i_ElementCount,
          OUT=>#q_DestArray[0]);

// Fill Block (Initialize Array with a specific value)
FILL_BLK(IN:=#i_FillValueByte,
         COUNT:=#i_ElementCount,
         OUT=>#q_DestArray[0]);

// Uninterruptible Fill Block
UFILL_BLK(IN:=#i_FillValueByte,
          COUNT:=#i_ElementCount,
          OUT=>#q_DestArray[0]);
```

## STRATEGY: WORD LOGIC OPERATIONS (DECO, ENCO, SEL, MUX)
Use these specific Siemens system functions for word-level logic operations. Always use the `#` prefix for local variables.

```scl
// Decode and Encode
#temp_Word := DECO(#i_UintVal);
#temp_Byte := ENCO(#i_ByteVal);

// Selection and Multiplexing
#temp_WChar := SEL(G:=#i_Condition, IN0:=#i_WCharFalse, IN1:=#i_WCharTrue);
#temp_Sint := MUX(K:=#i_Index, IN0:=#i_Val0, IN1:=#i_Val1);

// Demultiplexing
DEMUX(K:=#i_Index,
      IN:=#i_InputVal,
      OUT0=>#q_Out0,
      OUT1=>#q_Out1);
```

## STRATEGY: SHIFT AND ROTATE (SHR, SHL, ROR, ROL)
Use these specific Siemens system functions for bitwise shift and rotate operations.

```scl
// Shift Right / Left
#temp_DWord := SHR(IN:=#i_DWordVal, N:=#i_ShiftBits);
#temp_DWord := SHL(IN:=#i_DWordVal, N:=#i_ShiftBits);

// Rotate Right / Left
#temp_DWord := ROR(IN:=#i_DWordVal, N:=#i_RotateBits);
#temp_DWord := ROL(IN:=#i_DWordVal, N:=#i_RotateBits);
```
## STRATEGY: RUNTIME AND SYSTEM CONTROL (ENDIS_PW, GET_ERROR, RUNTIME)
Use these specific Siemens system functions for CPU runtime measurement, diagnostics, and access protection. Always use the `#` prefix for local variables.

```scl
// CPU Password Protection Control (ENDIS_PW)
ENDIS_PW(REQ := #i_EnableReq,
         F_PWD := #i_FailSafePwd,
         FULL_PWD := #i_FullAccessPwd,
         R_PWD := #i_ReadAccessPwd,
         HMI_PWD := #i_HmiAccessPwd,
         F_PWD_ON => #q_FailSafeActive,
         FULL_PWD_ON => #q_FullAccessActive,
         R_PWD_ON => #q_ReadAccessActive,
         HMI_PWD_ON => #q_HmiAccessActive);

// Diagnostics and Error Handling
GET_ERROR(#temp_ErrorStruct);
#temp_ErrorId := GET_ERR_ID();

// System State Control
STP(); // Puts the CPU into STOP mode
INIT_RD(#i_InitRead);
WAIT(#i_WaitTime);

// Runtime Measurement
RUNTIME(#iq_RuntimeMemory);
```

## STRATEGY: FUNCTION_BLOCK (FB) TEMPLATE
Use FB for device control, logic requiring memory (Timers, State Machines).
Critical Rule: Internal Timers/Triggers MUST be declared in `VAR` (Static) as Multi-instances. Variables inside `body_code` MUST use `#`.

```scl
FUNCTION_BLOCK "AUTO_GENERATE_FB_NAME"
{ S7_Optimized_Access := 'TRUE' }
   VAR_INPUT
      // [Prefix i_]: Control signals
   END_VAR
   VAR_OUTPUT
      // [Prefix q_]: Status signals
   END_VAR
   VAR_IN_OUT
      // [Prefix iq_]: Data modified inside
   END_VAR
   VAR
      // [Prefix stat_]: Internal Memory (Timers, Triggers, States)
      // Example: stat_Timer : TON;
   END_VAR
   VAR_TEMP
      // [Prefix temp_]: Intermediate calculations
   END_VAR
   VAR CONSTANT
      // [Prefix const_]: Fixed constants
   END_VAR
BEGIN
   // [WRITE ALL CONTROL LOGIC IN SCL HERE]
   // Always use #variable syntax (e.g., #q_Motor := #i_Start;)
END_FUNCTION_BLOCK
```

## STRATEGY: FUNCTION (FC) TEMPLATE
Use FC for pure calculation or logic where state is handled externally.
Critical Rule: FC has NO STATIC MEMORY. You CANNOT use `VAR`. All Timers/Triggers must be passed via `VAR_IN_OUT`. Variables inside `body_code` MUST use `#`.

```scl
FUNCTION "AUTO_GENERATE_FC_NAME" : Void
{ S7_Optimized_Access := 'TRUE' }
   VAR_INPUT
      // [Prefix i_]
   END_VAR
   VAR_OUTPUT
      // [Prefix q_]
   END_VAR
   VAR_IN_OUT
      // [Prefix iq_]: ALL Timers/Triggers must be passed here.
      // Example: iq_Timer : TON;
   END_VAR
   VAR_TEMP
      // [Prefix temp_]
   END_VAR
   VAR CONSTANT
      // [Prefix const_]
   END_VAR
BEGIN
   // [WRITE ALL CONTROL LOGIC IN SCL HERE]
   // Remember: FCs do NOT have 'VAR' (Static).
END_FUNCTION
```

## STRATEGY: ORGANIZATION_BLOCK (OB) WIRING TEMPLATE
Use OB for the Main Cycle to wire FBs together. 
CRITICAL RULES:
1. OB has NO STATIC MEMORY (Only `VAR_TEMP`). 
2. You must call FBs or Timers using Single Instance (Global DB). 
3. **NAMING CONVENTION FOR INSTANCE DB:** The DB name MUST follow this exact structure: `"Inst_[Exact_FB_Name]_[Optional_Suffix]"`. 
   - Example 1: If FB is `FB_Pump`, the DB must be `"Inst_FB_Pump_01"`.
   - Example 2: If FB is `FB_Conveyor`, the DB must be `"Inst_FB_Conveyor_Main"`.
   - DO NOT use `#` when calling a Global DB.

```scl
ORGANIZATION_BLOCK "AUTO_GENERATED_OB_NAME"
   VAR_TEMP
      // [Prefix temp_]: Intermediate logic flags
   END_VAR
BEGIN
   // Example: Wiring FB_MainConveyorControl to physical tags
   // We use "Inst_FB_MainConveyorControl" WITHOUT the '#' prefix.
   "Inst_FB_MainConveyorControl"(i_RunCmd := "Tag_StartBtn", 
                                 q_ConveyorRun => "Tag_Motor");
   
   // Example: Calling a Global Timer
   "Timer_DB_1".TON(IN := "Tag_Sensor", PT := T#5s);
END_ORGANIZATION_BLOCK
```

## STRATEGY: DATA_BLOCK (INSTANCE DB) TEMPLATE
Required when using FBs in an OB. You must generate a Data Block for every FB instance.

```scl
DATA_BLOCK "AUTO_GEN_NAME_DB"
NON_RETAIN
"Actual_FB_Name_Here"
BEGIN
   // Values initialization (Optional)
END_DATA_BLOCK
```