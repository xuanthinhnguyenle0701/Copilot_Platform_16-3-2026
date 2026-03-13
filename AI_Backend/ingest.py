import os
import chromadb
from langchain_huggingface import HuggingFaceEmbeddings
from langchain_community.vectorstores import Chroma
from langchain_core.documents import Document 
import app_secrets

def ingest_knowledge():
    print("🚀 Starting data ingestion...")

    # 1. Khởi tạo Client trỏ vào thư mục DB
    persistent_client = chromadb.PersistentClient(path=app_secrets.CHROMA_DB_PATH)

    # --- SỬA LỖI Ở ĐÂY: KHÔNG XÓA FOLDER NỮA, CHỈ XÓA ĐÚNG COLLECTION CỦA KB ---
    try:
        persistent_client.delete_collection("iec_standard_kb")
        print("🗑️  Removed old KB collection successfully (Spec collection is kept safe).")
    except Exception:
        print("ℹ️  No old KB collection found. Creating a new one.")

    # 2. Đọc file Markdown
    file_path = "data/scl_siemens_base.md"
    if not os.path.exists(file_path):
        print(f"❌ Error: Could not find file {file_path}")
        return

    with open(file_path, "r", encoding="utf-8") as f:
        content = f.read()

    # 3. Tách lấy phần RAG Context
    marker = "# [PART 2] RAG_CONTEXT"
    if marker not in content:
        print("❌ Error: Could not find marker '[PART 2] RAG_CONTEXT'.")
        return

    rag_content = content.split(marker)[1]
    
    # 4. Cắt nhỏ dữ liệu
    raw_chunks = rag_content.split("## STRATEGY:")
    documents = []
    
    print("✂️  Chunking data...")
    for chunk in raw_chunks:
        if not chunk.strip(): continue
        
        full_text = "STRATEGY:" + chunk.strip()
        
        doc_type = "SYNTAX"
        if "FUNCTION_BLOCK" in full_text: doc_type = "COMPONENT"
        elif "FUNCTION " in full_text: doc_type = "COMPONENT"
        elif "DATA_BLOCK" in full_text: doc_type = "COMPONENT"
        elif "ORGANIZATION_BLOCK" in full_text: doc_type = "SYSTEM"
        
        doc = Document(page_content=full_text, metadata={"type": doc_type, "source": "scl_siemens_base.md"})
        documents.append(doc)
        print(f"   🔹 Prepared: {doc_type} - {full_text[:30]}...")

    # 5. Khởi tạo Vector DB với tên Collection rõ ràng
    print("🧠 Ingesting to ChromaDB...")
    embedding_model = HuggingFaceEmbeddings(model_name="all-MiniLM-L6-v2")
    
    db = Chroma.from_documents(
        documents=documents,
        embedding=embedding_model,
        persist_directory=app_secrets.CHROMA_DB_PATH,
        collection_name="iec_standard_kb"
    )
    
    print(f"✅ Successfully ingested {len(documents)} documents into 'iec_standard_kb'!")

if __name__ == "__main__":
    ingest_knowledge()