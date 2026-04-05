import sys
import json
import os
import logging
import warnings
import memory 
import app_secrets

os.environ["CHROMA_TELEMETRY_IMPL"] = "none"
warnings.filterwarnings("ignore")
logging.getLogger("chromadb").setLevel(logging.ERROR)
logging.getLogger("sentence_transformers").setLevel(logging.ERROR)
logging.getLogger("langchain").setLevel(logging.ERROR)

        # --- IMPORT LANGCHAIN & CHROMA ---
from langchain_google_genai import ChatGoogleGenerativeAI
from langchain_huggingface import HuggingFaceEmbeddings 
from langchain_community.vectorstores import Chroma
from langchain_text_splitters import RecursiveCharacterTextSplitter
import chromadb
import google.generativeai as genai
      

os.environ["GOOGLE_API_KEY"] = app_secrets.GEMINI_API_KEY

def get_system_prompt_part1():
    try:
        with open("data/scl_siemens_base.md", "r", encoding="utf-8") as f:
            return f.read().split("# [PART 2] RAG_CONTEXT")[0]
    except:
        return "You are an expert IEC 61131-3 Programmer."

def get_hmi_system_prompt():
    try:
        with open("data/hmi_siemens_base.md", "r", encoding="utf-8") as f:
            return f.read().split("# [PART 2] RAG_CONTEXT")[0]
    except:
        return "You are an expert Siemens WinCC Unified HMI screen designer."

def get_cwc_system_prompt():
    try:
        with open("data/cwc_siemens_base.md", "r", encoding="utf-8") as f:
            return f.read().split("# [PART 2] RAG_CONTEXT")[0]
    except:
        return "You are an expert Siemens WinCC Unified Custom Web Control developer."

def get_output_schema(target_block_type="AUTO"):
    if target_block_type == "HMI_SCREEN":
        schema_file = "data/siemenshmi_output_schema.json"
        fallback = '{"screen_info": {"name": "AI_Screen", "width": 1024, "height": 600}, "items": [], "global_tags": []}'
    elif target_block_type == "CWC_SCREEN":
        schema_file = "data/cwc_output_schema.json"
        fallback = '{"cwc_info": {"name": "AI_Control", "displayname": "AI Control", "description": ""}, "properties": [], "events": [], "methods": [], "third_party_libs": [], "html_content": "", "js_content": "", "css_content": ""}'
    else:
        schema_file = "data/siemensplc_output_schema.json"
        fallback = """
        {
          "block_info": { "name": "FB_Standard", "type": "FUNCTION_BLOCK", "description": "Standard Block" },
          "interface": [
            { "name": "i_Enable", "type": "BOOL", "direction": "VAR_INPUT", "description": "Enable input" },
            { "name": "q_Ready", "type": "BOOL", "direction": "VAR_OUTPUT", "description": "Ready output" },
            { "name": "stat_Timer", "type": "TON", "direction": "VAR", "description": "Internal Timer" }
          ],
          "body_code": "#stat_Timer(IN := #i_Enable, PT := T#1s);\\n #q_Ready := #stat_Timer.Q;",
          "global_tags": [
            { "name": "TAG_StartBtn_01", "type": "BOOL", "comment": "Input" },
            { "name": "TAG_MotorSpeed_01", "type": "REAL", "comment": "Output" },
            { "name": "TAG_SystemFlag", "type": "BOOL", "comment": "Memory" }
          ]
        }
        """
    try:
        with open(schema_file, "r", encoding="utf-8") as f:
            return f.read()
    except:
        return fallback

def send_response(data_dict):
    """Ép xả dữ liệu thẳng xuống ống nước (Buffer) và cưỡng bức tắt Python ngay lập tức"""
    output_bytes = (json.dumps(data_dict, ensure_ascii=False) + "\n").encode('utf-8')
    sys.stdout.buffer.write(output_bytes)
    sys.stdout.buffer.flush()
    os._exit(0)

def clean_json_response(text):
    cleaned = text.strip()
    if cleaned.startswith("```json"):
        cleaned = cleaned.replace("```json", "", 1)
    if cleaned.startswith("```"):
        cleaned = cleaned.replace("```", "", 1)
    if cleaned.endswith("```"):
        cleaned = cleaned[:-3]
    return cleaned.strip()

def main():
    # SỬA: Dùng 'utf-8-sig' để Python tự gọt BOM nếu C# gửi sang dính kèm
    sys.stdin.reconfigure(encoding='utf-8-sig') 
    sys.stdout.reconfigure(encoding='utf-8')

    try:
        # Đọc dữ liệu và gọt thêm một lần nữa cho chắc ăn
        input_raw = sys.stdin.read().strip().lstrip('\ufeff')
        if not input_raw: return

        request_data = json.loads(input_raw)
        if not input_raw: return

        request_data = json.loads(input_raw)
        user_query = request_data.get("query", "")
        session_id = request_data.get("session_id", "default")
        command_type = request_data.get("command", "chat") 
        context_code = request_data.get("context_code", "")
        user_tags = request_data.get("user_tags", "").strip()
        spec_text = request_data.get("spec_text", "").strip()
        target_block_type = request_data.get("target_block_type", "AUTO").upper()

        # region XỬ LÝ LỆNH ĐẶC BIỆT (KHÔNG PHẢI CHAT) - LIST SESSIONS, RESET SESSION, UPDATE SPEC, CHECK SPEC

        

        if command_type == "list_sessions":
            sessions = memory.list_all_sessions()
            send_response({"status": "success", "sessions": sessions})

        if command_type == "create_session":
            memory.init_session(session_id)
            send_response({"status": "success", "message": f"Created {session_id}"})

        if command_type == "reset":
            memory.clear_session(session_id)
            send_response({"status": "success", "message": f"Session '{session_id}' cleared."})

        # --- BƯỚC 2: XỬ LÝ LỆNH UPDATE SPEC ---
        if command_type == "update_spec":
            try:
                persistent_client = chromadb.PersistentClient(path=app_secrets.CHROMA_DB_PATH)
                try:
                    persistent_client.delete_collection("current_project_spec")
                except Exception:
                    pass 
                
                if spec_text:
                    splitter = RecursiveCharacterTextSplitter(chunk_size=500, chunk_overlap=50)
                    chunks = splitter.split_text(spec_text)
                    
                    embeddings = HuggingFaceEmbeddings(model_name="all-MiniLM-L6-v2")
                    Chroma.from_texts(
                        texts=chunks, 
                        embedding=embeddings, 
                        persist_directory=app_secrets.CHROMA_DB_PATH,
                        collection_name="current_project_spec"
                    )
                    msg = f"Chunked and loaded {len(chunks)} chunks Spec into Vector DB."
                else:
                    msg = "Deleted old Spec. Current system has no Spec constraints."
                    
                send_response({"status": "success", "message": msg})
            except Exception as e:
                send_response({"status": "error", "message": f"Error loading Spec: {str(e)}"})
        if command_type == "check_spec":
            try:
                persistent_client = chromadb.PersistentClient(path=app_secrets.CHROMA_DB_PATH)
                try:
                    collection = persistent_client.get_collection("current_project_spec")
                    results = collection.get()
                    docs = results.get("documents", [])
                    
                    if not docs:
                        msg = "No current spec found. The system is empty."
                    else:
                        preview_text = "\n\n--- [CHUNK NEXT] ---\n\n".join(docs)
                        msg = f"Found {len(docs)} chunks in current Spec.\n\n[CURRENT SPEC CONTENT]:\n{preview_text}"
                except Exception:
                    msg = "No current Spec collection found. The system is completely empty."
                    
                send_response({"status": "success", "message": msg})
            except Exception as e:
                send_response({"status": "error", "message": f"Error reading Spec: {str(e)}"})
                
        # --- LỆNH DỌN DẸP VECTOR DB (SPEC) ---
        elif command_type == "clear_spec":
            try:
                import shutil
                # SỬA LỖI Ở ĐÂY: Trỏ thẳng vào thư mục CHROMA_DB_PATH trong app_secrets
                db_directory = app_secrets.CHROMA_DB_PATH 

                if os.path.exists(db_directory):
                    shutil.rmtree(db_directory)
                    msg = "Đã xóa toàn bộ Spec cũ. Database đã được làm sạch!"
                else:
                    msg = "Hệ thống đang trống, không có Spec nào để xóa."

                send_response({"status": "success", "message": msg})
            except Exception as e:
                send_response({"status": "error", "message": f"Lỗi khi xóa Spec: {str(e)}"})
        # endregion
        
        # region DUAL-PATH RAG RETRIEVAL — branches on target_block_type
        embeddings = HuggingFaceEmbeddings(model_name="all-MiniLM-L6-v2")
        kb_context = ""
        spec_context = ""

        if target_block_type == "CWC_SCREEN":
            # --- CWC PATH: retrieve from cwc_standard_kb ---
            # Smart filter: match query keywords to chunk types
            # PROPERTY covers property declaration rules
            # EVENT    covers event/method patterns
            # UI       covers visual element patterns (gauge, button, chart, table)
            # LIFECYCLE covers WebCC API lifecycle patterns
            cwc_search_kwargs = {"k": 5}

            query_upper = user_query.upper()
            cwc_filter = {}
            if any(w in query_upper for w in ["GAUGE", "CHART", "TABLE", "BUTTON", "INDICATOR", "BAR", "LED", "DISPLAY"]):
                cwc_filter = {"type": {"$in": ["UI", "LIFECYCLE"]}}
            elif any(w in query_upper for w in ["PROPERTY", "TAG", "BOOL", "NUMBER", "STRING", "REAL", "INT"]):
                cwc_filter = {"type": {"$in": ["PROPERTY", "EVENT"]}}
            elif any(w in query_upper for w in ["EVENT", "METHOD", "FIRE", "CLICK", "PRESS"]):
                cwc_filter = {"type": {"$in": ["EVENT", "UI"]}}

            if cwc_filter:
                cwc_search_kwargs["filter"] = cwc_filter

            try:
                cwc_kb_db = Chroma(
                    persist_directory=app_secrets.CHROMA_DB_PATH,
                    embedding_function=embeddings,
                    collection_name="cwc_standard_kb"
                )
                cwc_retriever = cwc_kb_db.as_retriever(search_kwargs=cwc_search_kwargs)
                cwc_docs = cwc_retriever.invoke(user_query)
                kb_context = "\n\n".join([d.page_content for d in cwc_docs])
            except Exception:
                kb_context = ""  # cwc_standard_kb not ingested yet — prompt still works without it

        elif target_block_type == "HMI_SCREEN":
            # --- HMI PATH: retrieve from hmi_standard_kb ---
            # Smart filter: query drives which object categories to pull
            # WIDGET covers library objects (Tank/Valve/Motor/Pipe/shapes)
            # CONTROL covers I-O controls and data panels
            # SCREEN covers navigation and screen structure
            # LAYOUT covers general composition guidance
            hmi_search_kwargs = {"k": 5}  # Pull more chunks — HMI queries are broad

            query_upper = user_query.upper()
            hmi_filter = {}
            if any(w in query_upper for w in ["TANK", "VALVE", "MOTOR", "PUMP", "PIPE", "SENSOR", "INDICATOR"]):
                hmi_filter = {"type": {"$in": ["WIDGET", "LAYOUT"]}}
            elif any(w in query_upper for w in ["TREND", "ALARM", "RECIPE", "DIAGNOSIS", "CHART"]):
                hmi_filter = {"type": {"$in": ["CONTROL", "SCREEN"]}}
            elif any(w in query_upper for w in ["BUTTON", "NAVIGATE", "SCREEN", "WINDOW"]):
                hmi_filter = {"type": {"$in": ["SCREEN", "CONTROL", "LAYOUT"]}}

            if hmi_filter:
                hmi_search_kwargs["filter"] = hmi_filter

            try:
                hmi_kb_db = Chroma(
                    persist_directory=app_secrets.CHROMA_DB_PATH,
                    embedding_function=embeddings,
                    collection_name="hmi_standard_kb"
                )
                hmi_retriever = hmi_kb_db.as_retriever(search_kwargs=hmi_search_kwargs)
                hmi_docs = hmi_retriever.invoke(user_query)
                kb_context = "\n\n".join([d.page_content for d in hmi_docs])
            except Exception:
                kb_context = ""  # hmi_standard_kb not ingested yet — prompt still works without it

        else:
            # --- SCL PATH: retrieve from iec_standard_kb (original logic, unchanged) ---
            kb_db = Chroma(
                persist_directory=app_secrets.CHROMA_DB_PATH,
                embedding_function=embeddings,
                collection_name="iec_standard_kb"
            )
            search_kwargs = {"k": 4}
            filter_dict = {}

            if target_block_type in ["FB", "FC"]:
                filter_dict = {"type": {"$in": ["COMPONENT", "SYNTAX"]}}
            elif target_block_type == "OB":
                filter_dict = {"type": {"$in": ["SYSTEM", "SYNTAX"]}}

            if filter_dict:
                search_kwargs["filter"] = filter_dict

            kb_retriever = kb_db.as_retriever(search_kwargs=search_kwargs)
            kb_docs = kb_retriever.invoke(user_query)
            kb_context = "\n\n".join([d.page_content for d in kb_docs])

        # Spec context — shared by both paths (project operational rules)
        try:
            spec_db = Chroma(
                persist_directory=app_secrets.CHROMA_DB_PATH,
                embedding_function=embeddings,
                collection_name="current_project_spec"
            )
            spec_retriever = spec_db.as_retriever(search_kwargs={"k": 2})
            spec_docs = spec_retriever.invoke(user_query)
            if spec_docs:
                spec_context = "\n\n".join([d.page_content for d in spec_docs])
        except Exception:
            pass
        # endregion
        
        # region Prompt assembly — branches on target_block_type
        block_type_constraint = ""
        if target_block_type not in ["AUTO", "HMI_SCREEN", "CWC_SCREEN"]:
            block_type_constraint = f"""
            ### HARD CONSTRAINT - FORCED BLOCK TYPE:
            You MUST set the "type" field to "{target_block_type}".
            Your code MUST be formatted according to the rules of a {target_block_type}.
            """

        user_tags_constraint = ""
        if user_tags and target_block_type in ["OB", "ORGANIZATION_BLOCK"]:
            user_tags_constraint = f"""
        ### 🎯 USER DEFINED I/O TAGS (STRICT WIRING DICTIONARY):
        The user has provided a specific list of physical I/O tags. 
        When you are writing code to call a Function Block (FB) or Function (FC) inside an OB, you MUST map the inputs/outputs ONLY to the tags listed below.
        DO NOT invent, fabricate, or guess new global tag names. Choose the most logically appropriate tag from this list based on its name and Data Type.
        
        [AVAILABLE TAGS]:
        {user_tags}
            """

        chat_history_str = memory.get_sliding_window_context(session_id, window_size=5)
        system_rules = get_system_prompt_part1()
        target_schema = get_output_schema(target_block_type)

        if target_block_type == "CWC_SCREEN":
            cwc_system_rules = get_cwc_system_prompt()

            cwc_tags_constraint = ""
            if user_tags:
                cwc_tags_constraint = f"""
        ### 🎯 AVAILABLE PLC TAGS — USE THESE AS PROPERTY NAMES:
        The following tags exist on the PLC. When declaring properties in the "properties" array,
        name them after the relevant tags below. The same names MUST appear in your js_content
        when calling WebCC.onPropertyChanged and WebCC.Properties.
        DO NOT invent tag names that are not in this list.

        [AVAILABLE TAGS]:
        {user_tags}
                """

            full_prompt = f"""
        {cwc_system_rules}

        {cwc_tags_constraint}

        ### 🛑 PROJECT OPERATIONAL REQUIREMENTS (MUST FOLLOW):
        {spec_context}

        ### 📚 CWC OBJECT REFERENCE (RETRIEVED FROM KNOWLEDGE BASE):
        Use these rules to select correct property types, event patterns, and UI element implementations.
        {kb_context}

        ### CHAT HISTORY:
        {chat_history_str}

        ### REQUIRED JSON OUTPUT SCHEMA:
        You MUST return JSON that EXACTLY matches this structure.
        Remove all "_comment" and "_comment_*" fields from your output — they are reference only.

        {target_schema}

        ### ⚙️ CRITICAL RULES (VIOLATIONS BREAK THE CONTROL):

        1. **WebCC.start() — WRITE IT COMPLETELY IN js_content:**
           - js_content must contain the FULL WebCC.start() call.
           - The contract object inside WebCC.start() MUST match your declared arrays:
             - methods: object with REAL function implementations (not empty stubs)
             - events: array of event name strings exactly as declared in "events"
             - properties: object with default values exactly as declared in "properties"
           - Pattern: WebCC.start(function(result){{ if(result){{ init(); WebCC.onPropertyChanged.subscribe(setProperty); }} }}, {{ methods:{{...}}, events:[...], properties:{{...}} }}, [], 10000);

        2. **NAME CONSISTENCY (CRITICAL — case-sensitive):**
           - Every name in "properties" array → used in WebCC.Properties.Name AND in the properties object inside WebCC.start()
           - Every name in "events" array → used in WebCC.Events.fire("Name") AND in events array inside WebCC.start()
           - Every name in "methods" array → implemented as a function in methods object inside WebCC.start()
           - One mismatch silently breaks TIA Portal tag binding.

        3. **PROPERTY TYPES:**
           - "boolean" → BOOL PLC tags. Default value must be false (not "false").
           - "number"  → INT, REAL, DINT tags. Default value must be a number (not string).
           - "string"  → STRING tags. Default value must be "" (empty string).

        4. **HTML STRUCTURE (MANDATORY LOAD ORDER):**
           - In <head>: webcc.min.js FIRST, then third-party libs, then styles.css
           - At END of <body>: code.js ONLY
           - No inline JS anywhere in html_content.
           - Give every interactive element a unique id attribute.

        5. **THIRD-PARTY LIBRARIES:**
           - Only declare in "third_party_libs" if user explicitly requests one OR the UI requires it.
           - Use filenames only (e.g. "gauge.min.js"). File must exist in cwc_assets/ folder.
           - If none needed: "third_party_libs": []
           - Load in html_content as: <script src='./js/gauge.min.js'></script>

        6. **DESIGN MODE GUARD (MUST INCLUDE):**
           - Inside the WebCC.start() success callback, check design mode first:
             if (WebCC.isDesignMode) {{ showPlaceholder(); return; }}
           - showPlaceholder() renders a static labeled preview of the control.

        7. **RESPONSIVE SIZING:**
           - body: width:100%; height:100%; overflow:hidden; margin:0
           - Use %, flex, or canvas resize logic — no hardcoded px on outer containers.

        8. **CSS STYLE:**
           - Default: dark industrial (#1a1a2e background, high contrast text).
           - Unless the user requests a specific color scheme.

        9. **cwc_info NAME FIELD:**
           - PascalCase, underscores for spaces. Example: "Tank_Level_Monitor".
           - This becomes the control's display name in TIA Portal Toolbox.

        ### USER REQUEST:
        {user_query}

        GENERATE JSON ONLY. No markdown, no explanation, no code fences.
        """
        elif target_block_type == "HMI_SCREEN":
            hmi_system_rules = get_hmi_system_prompt()

            hmi_tags_constraint = ""
            if user_tags:
                hmi_tags_constraint = f"""
        ### 🎯 AVAILABLE PLC TAGS — STRICT BINDING DICTIONARY:
        The following tags exist on the PLC. When you bind an HMI object to a tag, you MUST
        choose ONLY from this list. Do NOT invent or fabricate tag names.
        Map each object to the most logically appropriate tag based on its name and data type.

        [AVAILABLE TAGS]:
        {user_tags}
                """

            full_prompt = f"""
        {hmi_system_rules}

        {hmi_tags_constraint}

        ### 🛑 PROJECT OPERATIONAL REQUIREMENTS (MUST FOLLOW):
        This is the project spec. Every screen object and tag binding MUST align with these rules.
        {spec_context}

        ### 📚 HMI OBJECT REFERENCE (RETRIEVED FROM KNOWLEDGE BASE):
        Use these rules to select correct object types, subtypes, behaviors, and field names.
        {kb_context}

        ### CHAT HISTORY:
        {chat_history_str}

        ### REQUIRED JSON OUTPUT SCHEMA:
        You MUST return JSON that EXACTLY matches this structure. Do not add, remove, or rename any keys.
        Every item in the "items" array MUST have at minimum: "name", "type".
        Remove the "_comment_*" fields — those are for your reference only, do NOT include them in output.

        {target_schema}

        ### ⚙️ CRITICAL RULES (MUST FOLLOW — VIOLATIONS WILL BREAK THE ASSEMBLER):

        1. **LOGICAL JSON ONLY — NO PHYSICAL DATA:**
           - Do NOT include pixel coordinates (Left, Top, Width, Height).
           - Do NOT include LibraryPath strings.
           - Do NOT write any JavaScript (ColorScript, KeyDown/KeyUp script strings).
           - The C# assembler handles all of the above. Your job is intent and tag binding only.

        2. **TAG BINDING RULE:**
           - Use "bind_tag" for the primary tag that drives the object's state or value.
           - If no tags are provided, invent logical tag names that match the process described.
           - For TrendControl, use "trend_tag" instead of "bind_tag".
           - AlarmControl, FunctionTrendControl, SystemDiagnosisControl do NOT need a tag.

        3. **BEHAVIOR KEYWORDS — ONLY USE EXACT STRINGS:**
           - "fill_level"       → analog tag drives a visible fill (use for Tank, Bar)
           - "color_on_status"  → boolean tag drives green/red color change (use for Valve, Motor, Rectangle, Circle)
           - Do NOT invent other behavior keywords.

        4. **BUTTON RULES:**
           - Momentary button (START/STOP/RESET): include "keydown_write" and "keyup_write" with tag + value.
           - Navigation button (screen switch): include "navigate_to" with exact target screen name. No write fields.
           - Never combine both patterns on the same button.

        5. **SUBTYPE RULE:**
           - Valve: "ControlValve" or "GateValve"
           - Motor: "Motor2" (horizontal) or "Motor9Vertical" (vertical)
           - Pipe: "PipeHorizontal" or "PipeVertical"
           - Tank, Rectangle, Circle: no subtype needed.

        6. **HINT FIELD:**
           - Every object MUST have a "hint" field describing its intended zone and role.
           - Format: "<zone>, <role>". Example: "left sidebar, START button for conveyor pump"
           - Zones: "center process area", "left sidebar", "top status bar", "right indicator column",
             "bottom navigation bar", "top-left monitoring panel", "top-right monitoring panel"

        7. **NAMING CONVENTION:**
           - Use underscore-separated Vietnamese or English names. No spaces.
           - Example: "Bon_Chua_Chinh", "Nut_START", "Van_Cap_Vao_01"

        8. **GLOBAL TAGS:**
           - If the user's request implies new HMI-only tags (not in the PLC list), declare them in "global_tags".
           - Each entry needs: "name" (string), "type" (BOOL/INT/REAL), "comment" (purpose description).
           - If no new tags are needed, return "global_tags": [].

        9. **SCREEN INFO:**
           - Always fill "screen_info" with a meaningful "name", and default width/height of 1024x600
             unless the user specifies otherwise.

        ### USER REQUEST:
        {user_query}

        GENERATE JSON ONLY. No markdown, no explanation, no code fences.
        """

        else:
            full_prompt = f"""
        {system_rules}

        {user_tags_constraint}

        {block_type_constraint}
        
        ### 🛑 HARD CONSTRAINTS & OPERATIONAL LOGIC (MUST FOLLOW):
        # Đây là Spec dự án. BẠN BẮT BUỘC PHẢI DỰA VÀO ĐÂY ĐỂ VIẾT LOGIC.
        {spec_context}
        
        ### 📚 REFERENCE STANDARDS (GUIDELINES ONLY):
        # Đây là tiêu chuẩn IEC. CHỈ DÙNG ĐỂ THAM KHẢO CÚ PHÁP.
        {kb_context}
        
        ### CHAT HISTORY:
        {chat_history_str}

        ### CURRENT FILE CONTEXT (The code user is working on):
        {context_code}
        
        ### REQUIRED JSON SCHEMA:
        You MUST strictly follow this JSON structure. Do not change keys or nesting.
        
        {target_schema}
        
        **CRITICAL RULES (MUST FOLLOW):**
        1. **NAMING CONVENTION:** Use Hungarian Notation (i_, q_, iq_, stat_, temp_).
        2. **JSON MAPPING (CRITICAL):** - ALL variables MUST be defined ONLY in the "interface" array.
           - The "body_code" string is STRICTLY for executable logic ONLY. YOU MUST NOT write `VAR`, `END_VAR`, `VAR_TEMP`, or `BEGIN` inside "body_code".
        3. **SIEMENS SCL SYNTAX (FB/FC):** In "body_code", you MUST prefix ALL local variables with `#` (e.g., `#q_Motor := #stat_Timer.Q;`).
        4. **OB STRICT RULES & ANTI-OVERRIDE:** - In an ORGANIZATION_BLOCK (OB), you CANNOT declare instances in the interface. 
           - To call an FB, you MUST use its Global Data Block name in double quotes. 
           - **CRITICAL FORMAT:** The DB name MUST ALWAYS be formatted exactly as `"Inst_<FB_Name>__<Instance_Name>"`. 
           - **EXAMPLE:** If the FB is named "FB_WaterPump" and the user wants to call it "Pump 1", you MUST write `"Inst_FB_WaterPump__Pump1"`. 
           - You MUST use a DOUBLE UNDERSCORE (`__`) to separate the FB_Name and the Instance_Name. DO NOT use a single underscore here. NEVER drop the FB type from the DB name.
           - DO NOT use the `#` prefix for Global DB calls and Global Tags in OB. ONLY use `#` for local variables inside FB/FC body_code.
           - **GLOBAL TAG WIRING RULE:** When wiring inputs/outputs to the FB in an OB, if you create new global tags, you MUST prefix them with `TAG_` and put them in double quotes (e.g., `"TAG_StartBtn_1"`, `"TAG_MainConveyor_Out"`).
        5. **STATE MACHINE & TIMERS (CRITICAL):** NEVER call the same Timer (TON/TOF) or Counter (CTU/CTD) multiple times inside IF or CASE statements. You MUST call them EXACTLY ONCE at the end of the "body_code". Use internal flags to trigger their inputs.
        6. **MATH & ANALOG RULE (CRITICAL):** NORM_X, SCALE_X, ABS, MIN, MAX are built-in functions. DO NOT declare them in the interface (VAR). Call them directly and assign their return value (e.g., `#temp_Real := SCALE_X(MIN:=0.0, VALUE:=#temp_Norm, MAX:=100.0);`). ALWAYS use 'VALUE' parameter, NOT 'IN'.
        7. **GLOBAL TAG DECLARATION:** If you generate an OB and create any global tags (with `TAG_` prefix), you MUST list ALL of them inside the `"global_tags"` JSON array along with their inferred "type" (e.g., "BOOL", "REAL", "INT").
        8. **GLOBAL TAG COMMENT:** Inside the "global_tags" array, you MUST add a "comment" field for each tag. Evaluate how the tag is wired in the OB: if it's wired to an input, label it "Input"; if to an output, label it "Output"; otherwise label it "Memory".
        
        ### USER REQUEST:
        {user_query}
        
        GENERATE JSON ONLY.
        """
        # endregion
        
        llm = ChatGoogleGenerativeAI(
            model="gemini-2.5-flash",
            temperature=0.1,
            convert_system_message_to_human=True
        )
        
        # Cấu hình API Key cho thư viện gốc
        genai.configure(api_key=app_secrets.GEMINI_API_KEY)
        
        # Khởi tạo object model chuẩn của Google (Chỉ dùng để đếm token)
        token_model = genai.GenerativeModel("gemini-2.5-flash")
        
        # Gọi hàm đếm token trực tiếp (Nếu sai key hoặc rớt mạng, nó sẽ báo lỗi thẳng về C#)
        token_count = token_model.count_tokens(full_prompt).total_tokens

        # 2. Gọi AI sinh code (Dùng Langchain)
        response = llm.invoke(full_prompt)
        final_json_str = clean_json_response(response.content)
        
        # 3. Bóc JSON ra, nhét token_count vào
        try:
            data_dict = json.loads(final_json_str)
            data_dict["token_usage"] = token_count
            final_json_str = json.dumps(data_dict, ensure_ascii=False, indent=2)
        except Exception as e:
            final_json_str = json.dumps({"error": f"Lỗi chèn Token: {str(e)}", "raw_output": final_json_str})

        # 4. Lưu lịch sử và in kết quả
        memory.save_turn(session_id, user_query, final_json_str)
        output_bytes = (final_json_str + "\n").encode('utf-8')
        sys.stdout.buffer.write(output_bytes)
        sys.stdout.buffer.flush()

    except Exception as e:
        error_res = {"status": "error", "message": str(e)}
        err_bytes = (json.dumps(error_res, ensure_ascii=False) + "\n").encode('utf-8')
        sys.stdout.buffer.write(err_bytes)
        sys.stdout.buffer.flush()

if __name__ == "__main__":
    main()