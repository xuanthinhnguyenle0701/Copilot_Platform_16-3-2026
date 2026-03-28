from pathlib import Path
import sqlite3
from langchain_community.chat_message_histories import SQLChatMessageHistory
from langchain_core.messages import HumanMessage, AIMessage

BASE_DIR = Path(__file__).resolve().parent
REAL_DB_FILE = BASE_DIR.parent / "Translator_CLI" / "chat_memory.db"
DB_PATH = f"sqlite:///{REAL_DB_FILE.as_posix()}"

def get_session_history(session_id):
    """Lấy object lịch sử của LangChain"""
    return SQLChatMessageHistory(
        session_id=session_id,
        connection_string=DB_PATH
    )

def get_sliding_window_context(session_id, window_size=3):
    """
    KỸ THUẬT SLIDING WINDOW:
    Chỉ lấy K cặp (User-AI) cuối cùng để tiết kiệm Token.
    """
    history = get_session_history(session_id)
    messages = history.messages
    
    if not messages:
        return ""
    
    # Lấy 2*K tin nhắn cuối cùng
    sliced_messages = messages[-(window_size * 2):]
    context_str = ""
    for msg in sliced_messages:
        if isinstance(msg, HumanMessage):
            context_str += f"User: {msg.content}\n"
        elif isinstance(msg, AIMessage):
            context_str += f"AI: {msg.content}\n"
            
    return context_str

def save_turn(session_id, user_input, ai_output):
    """Lưu hội thoại vào DB"""
    history = get_session_history(session_id)
    history.add_user_message(user_input)
    history.add_ai_message(ai_output)

def clear_session(session_id):
    """Xóa lịch sử của một session cụ thể"""
    history = get_session_history(session_id)
    history.clear()
    return True

def list_all_sessions():
    """
    Liệt kê tất cả các Session ID đang có trong Database.
    Hàm này dùng SQL thuần để quét bảng message_store.
    """
    try:
        conn = sqlite3.connect(str(REAL_DB_FILE))
        cursor = conn.cursor()
        # Bảng mặc định của LangChain thường tên là 'message_store'
        cursor.execute("SELECT DISTINCT session_id FROM message_store")
        rows = cursor.fetchall()
        conn.close()
        
        with open("session_debug.log", "a", encoding="utf-8") as f:
            f.write("DEBUG: Sessions found in DB: " + repr([row[0] for row in rows]) + "\n")
        # Trả về danh sách dạng ['session1', 'session2']
        return [row[0] for row in rows]
    except Exception as e:
        return [] # Trả về rỗng nếu DB chưa tồn tại hoặc lỗi
    
def init_session(session_id):
    """
    Ép DB tạo một Session bằng cách chèn một tin nhắn hệ thống ẩn.
    Giúp Session hiển thị ngay lập tức trong lệnh list_sessions.
    """
    history = get_session_history(session_id)
    # Nếu session đã có tin nhắn thì không cần mồi thêm
    if len(history.messages) == 0:
        history.add_ai_message("[SYSTEM_INIT] Session created.")
    return True