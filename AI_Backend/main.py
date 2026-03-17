import sys
import json
import os
import logging
import warnings
import memory 
import app_secrets

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

def get_output_schema():
    try:
        with open("data/siemensplc_output_schema.json", "r", encoding="utf-8") as f:
            return f.read()
    except:
        return """
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
        
        # [CẬP NHẬT] Đổi biến nhận Payload khớp với C#
        spec_text = request_data.get("spec_text", "").strip()
        target_block_type = request_data.get("target_block_type", "AUTO").upper()

        # region XỬ LÝ LỆNH ĐẶC BIỆT (KHÔNG PHẢI CHAT) - LIST SESSIONS, RESET SESSION, UPDATE SPEC, CHECK SPEC

        

        if command_type == "list_sessions":
            sessions = memory.list_all_sessions()
            print(json.dumps({"status": "success", "sessions": sessions}, ensure_ascii=False))
            return

        if command_type == "create_session":
            memory.init_session(session_id)
            print(json.dumps({"status": "success", "message": f"Created {session_id}"}, ensure_ascii=False))
            return

        if command_type == "reset":
            memory.clear_session(session_id)
            print(json.dumps({"status": "success", "message": f"Session '{session_id}' cleared."}, ensure_ascii=False))
            return

        # --- [MỚI] BƯỚC 2: XỬ LÝ LỆNH UPDATE SPEC ---
        if command_type == "update_spec":
            try:
                persistent_client = chromadb.PersistentClient(path=app_secrets.CHROMA_DB_PATH)
                
                # Cố gắng xóa collection cũ (nếu có) để tránh bóng ma Vector
                try:
                    persistent_client.delete_collection("current_project_spec")
                except Exception:
                    pass 
                
                if spec_text:
                    # Băm nhỏ Spec bằng Sliding Window theo ký tự
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
                    
                print(json.dumps({"status": "success", "message": msg}, ensure_ascii=False))
            except Exception as e:
                print(json.dumps({"status": "error", "message": f"Error loading Spec: {str(e)}"}, ensure_ascii=False))
            return
        # --- [MỚI] LỆNH KIỂM TRA NỘI DUNG SPEC ĐANG NẠP ---
        if command_type == "check_spec":
            try:
                persistent_client = chromadb.PersistentClient(path=app_secrets.CHROMA_DB_PATH)
                try:
                    # Cố gắng kết nối vào Collection Spec
                    collection = persistent_client.get_collection("current_project_spec")
                    results = collection.get()
                    docs = results.get("documents", [])
                    
                    if not docs:
                        msg = "No current spec found. The system is empty."
                    else:
                        # Nối các chunk lại với nhau (Vì khi nạp ta đã băm nhỏ)
                        preview_text = "\n\n--- [CHUNK NEXT] ---\n\n".join(docs)
                        msg = f"Found {len(docs)} chunks in current Spec.\n\n[CURRENT SPEC CONTENT]:\n{preview_text}"
                except Exception:
                    msg = "No current Spec collection found. The system is completely empty."
                    
                print(json.dumps({"status": "success", "message": msg}, ensure_ascii=False))
            except Exception as e:
                print(json.dumps({"status": "error", "message": f"Error reading Spec: {str(e)}"}, ensure_ascii=False))
            return
        # [MỚI] LỆNH DỌN DẸP VECTOR DB (SPEC)
        # =====================================================================
        elif command_type == "clear_spec":
            try:
                import shutil
                
                # Tùy thuộc vào việc bạn đang lưu thư mục ChromaDB ở đâu, hãy sửa tên thư mục cho đúng.
                # Giả sử thư mục lưu Vector DB của bạn tên là "chroma_db" (hoặc "db") nằm cùng cấp với main.py
                db_directory = "chroma_db" # ---> [SỬA LẠI TÊN THƯ MỤC NẾU CẦN]

                if os.path.exists(db_directory):
                    # Xóa vật lý toàn bộ thư mục chứa Database
                    shutil.rmtree(db_directory)
                    msg = "Đã xóa toàn bộ Spec cũ. Database đã được làm sạch!"
                else:
                    msg = "Hệ thống đang trống, không có Spec nào để xóa."

                response = {"status": "success", "message": msg}
                print(json.dumps(response, ensure_ascii=False))
                sys.exit(0)
            except Exception as e:
                response = {"status": "error", "message": f"Lỗi khi xóa Spec: {str(e)}"}
                print(json.dumps(response, ensure_ascii=False))
                sys.exit(1)
        # endregion
        
        # region FILTER CHỌN KIỂU BLOCK MỤC TIÊU (FB/FC/OB) - DỰA TRÊN THÔNG TIN NGỪNG CỦA USER
        # --- [CẬP NHẬT] BƯỚC 3: DUAL RAG RETRIEVAL ---
        embeddings = HuggingFaceEmbeddings(model_name="all-MiniLM-L6-v2")
        
        # 1. Truy xuất Sách giáo khoa (Tĩnh - Mặc định)
        kb_db = Chroma(persist_directory=app_secrets.CHROMA_DB_PATH, embedding_function=embeddings, collection_name="iec_standard_kb")
        # Xây dựng bộ lọc thông minh dựa trên loại khối (FB/FC/OB)
        search_kwargs = {"k": 4} # Lấy 4 chunks để đảm bảo đủ kiến thức
        filter_dict = {}
        
        if target_block_type in ["FB", "FC"]:
            # Nếu viết hàm, chỉ lấy luật của Hàm (COMPONENT) và Cú pháp chung (SYNTAX). CẤM LẤY LUẬT CỦA OB.
            filter_dict = {"type": {"$in": ["COMPONENT", "SYNTAX"]}}
        elif target_block_type == "OB":
            # Nếu đi dây OB, chỉ lấy luật của Trạm (SYSTEM) và Cú pháp chung (SYNTAX). CẤM LẤY LUẬT CỦA FB.
            filter_dict = {"type": {"$in": ["SYSTEM", "SYNTAX"]}}
            
        if filter_dict:
            search_kwargs["filter"] = filter_dict

        kb_retriever = kb_db.as_retriever(search_kwargs=search_kwargs)
        kb_docs = kb_retriever.invoke(user_query)
        kb_context = "\n\n".join([d.page_content for d in kb_docs])

        # 2. Truy xuất Luật vận hành (Động - Collection riêng)
        spec_context = ""
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
            pass # An toàn bỏ qua nếu User chưa nạp Spec bao giờ
        # endregion
        
        # region Prompt assembly
        # --- LẮP RÁP PROMPT PHÂN QUYỀN TRỌNG LƯỢNG ---
        block_type_constraint = ""
        if target_block_type != "AUTO":
            block_type_constraint = f"""
            ### HARD CONSTRAINT - FORCED BLOCK TYPE:
            You MUST set the "type" field to "{target_block_type}".
            Your code MUST be formatted according to the rules of a {target_block_type}.
            """

        user_tags_constraint = "" 
        # Chỉ kích hoạt luật khi C# có gửi Tags VÀ User đang yêu cầu viết OB
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
        target_schema = get_output_schema()
    
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
            final_json_str = json.dumps(data_dict,flush=True, ensure_ascii=False, indent=2)
        except Exception:
            pass

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