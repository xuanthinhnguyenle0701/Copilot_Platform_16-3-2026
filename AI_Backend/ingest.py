import os
import chromadb
from langchain_huggingface import HuggingFaceEmbeddings
from langchain_community.vectorstores import Chroma
from langchain_core.documents import Document
import app_secrets

# Shared embedding model — initialized once, reused by both ingest functions
_embedding_model = None

def get_embedding_model():
    global _embedding_model
    if _embedding_model is None:
        _embedding_model = HuggingFaceEmbeddings(model_name="all-MiniLM-L6-v2")
    return _embedding_model


def ingest_scl_knowledge():
    """Ingests SCL/IEC 61131-3 knowledge from scl_siemens_base.md into 'iec_standard_kb' collection."""
    print("\n🔧 [SCL] Starting SCL knowledge ingestion...")

    persistent_client = chromadb.PersistentClient(path=app_secrets.CHROMA_DB_PATH)
    try:
        persistent_client.delete_collection("iec_standard_kb")
        print("🗑️  Removed old 'iec_standard_kb' collection. (Spec collection kept safe.)")
    except Exception:
        print("ℹ️  No old 'iec_standard_kb' found. Creating a new one.")

    file_path = "data/scl_siemens_base.md"
    if not os.path.exists(file_path):
        print(f"❌ Error: Could not find file {file_path}")
        return

    with open(file_path, "r", encoding="utf-8") as f:
        content = f.read()

    marker = "# [PART 2] RAG_CONTEXT"
    if marker not in content:
        print("❌ Error: Could not find marker '[PART 2] RAG_CONTEXT'.")
        return

    rag_content = content.split(marker)[1]
    raw_chunks = rag_content.split("## STRATEGY:")
    documents = []

    print("✂️  Chunking SCL data...")
    for chunk in raw_chunks:
        if not chunk.strip():
            continue

        full_text = "STRATEGY:" + chunk.strip()

        # Type classification for SCL chunks
        doc_type = "SYNTAX"
        if "FUNCTION_BLOCK" in full_text:  doc_type = "COMPONENT"
        elif "FUNCTION " in full_text:      doc_type = "COMPONENT"
        elif "DATA_BLOCK" in full_text:     doc_type = "COMPONENT"
        elif "ORGANIZATION_BLOCK" in full_text: doc_type = "SYSTEM"

        doc = Document(page_content=full_text, metadata={"type": doc_type, "source": "scl_siemens_base.md"})
        documents.append(doc)
        print(f"   🔹 [{doc_type}] {full_text[:50]}...")

    print("🧠 Ingesting SCL docs to ChromaDB...")
    Chroma.from_documents(
        documents=documents,
        embedding=get_embedding_model(),
        persist_directory=app_secrets.CHROMA_DB_PATH,
        collection_name="iec_standard_kb"
    )
    print(f"✅ SCL ingestion complete: {len(documents)} documents → 'iec_standard_kb'")


def ingest_hmi_knowledge():
    """Ingests HMI WinCC Unified knowledge from hmi_siemens_base.md into 'hmi_standard_kb' collection."""
    print("\n🖥️  [HMI] Starting HMI knowledge ingestion...")

    persistent_client = chromadb.PersistentClient(path=app_secrets.CHROMA_DB_PATH)
    try:
        persistent_client.delete_collection("hmi_standard_kb")
        print("🗑️  Removed old 'hmi_standard_kb' collection. (Other collections kept safe.)")
    except Exception:
        print("ℹ️  No old 'hmi_standard_kb' found. Creating a new one.")

    file_path = "data/hmi_siemens_base.md"
    if not os.path.exists(file_path):
        print(f"❌ Error: Could not find file {file_path}")
        return

    with open(file_path, "r", encoding="utf-8") as f:
        content = f.read()

    marker = "# [PART 2] RAG_CONTEXT"
    if marker not in content:
        print("❌ Error: Could not find marker '[PART 2] RAG_CONTEXT'.")
        return

    rag_content = content.split(marker)[1]
    raw_chunks = rag_content.split("## STRATEGY:")
    documents = []

    print("✂️  Chunking HMI data...")
    for chunk in raw_chunks:
        if not chunk.strip():
            continue

        full_text = "STRATEGY:" + chunk.strip()

        # Type classification for HMI chunks — mirrors METADATA_TYPE tags in the .md
        doc_type = "LAYOUT"
        if "METADATA_TYPE: WIDGET" in full_text:   doc_type = "WIDGET"
        elif "METADATA_TYPE: CONTROL" in full_text: doc_type = "CONTROL"
        elif "METADATA_TYPE: SCREEN" in full_text:  doc_type = "SCREEN"

        doc = Document(page_content=full_text, metadata={"type": doc_type, "source": "hmi_siemens_base.md"})
        documents.append(doc)
        print(f"   🔹 [{doc_type}] {full_text[:50]}...")

    print("🧠 Ingesting HMI docs to ChromaDB...")
    Chroma.from_documents(
        documents=documents,
        embedding=get_embedding_model(),
        persist_directory=app_secrets.CHROMA_DB_PATH,
        collection_name="hmi_standard_kb"
    )
    print(f"✅ HMI ingestion complete: {len(documents)} documents → 'hmi_standard_kb'")


def ingest_cwc_knowledge():
    """Ingests WinCC Unified CWC knowledge from cwc_siemens_base.md into 'cwc_standard_kb' collection."""
    print("\n🌐 [CWC] Starting CWC knowledge ingestion...")

    persistent_client = chromadb.PersistentClient(path=app_secrets.CHROMA_DB_PATH)
    try:
        persistent_client.delete_collection("cwc_standard_kb")
        print("🗑️  Removed old 'cwc_standard_kb' collection. (Other collections kept safe.)")
    except Exception:
        print("ℹ️  No old 'cwc_standard_kb' found. Creating a new one.")

    file_path = "data/cwc_siemens_base.md"
    if not os.path.exists(file_path):
        print(f"❌ Error: Could not find file {file_path}")
        return

    with open(file_path, "r", encoding="utf-8") as f:
        content = f.read()

    marker = "# [PART 2] RAG_CONTEXT"
    if marker not in content:
        print("❌ Error: Could not find marker '[PART 2] RAG_CONTEXT'.")
        return

    rag_content = content.split(marker)[1]
    raw_chunks = rag_content.split("## STRATEGY:")
    documents = []

    print("✂️  Chunking CWC data...")
    for chunk in raw_chunks:
        if not chunk.strip():
            continue

        full_text = "STRATEGY:" + chunk.strip()

        # Type classification for CWC chunks — mirrors METADATA_TYPE tags in the .md
        doc_type = "LIFECYCLE"
        if "METADATA_TYPE: PROPERTY" in full_text:  doc_type = "PROPERTY"
        elif "METADATA_TYPE: EVENT" in full_text:   doc_type = "EVENT"
        elif "METADATA_TYPE: UI" in full_text:      doc_type = "UI"

        doc = Document(page_content=full_text, metadata={"type": doc_type, "source": "cwc_siemens_base.md"})
        documents.append(doc)
        print(f"   🔹 [{doc_type}] {full_text[:50]}...")

    print("🧠 Ingesting CWC docs to ChromaDB...")
    Chroma.from_documents(
        documents=documents,
        embedding=get_embedding_model(),
        persist_directory=app_secrets.CHROMA_DB_PATH,
        collection_name="cwc_standard_kb"
    )
    print(f"✅ CWC ingestion complete: {len(documents)} documents → 'cwc_standard_kb'")


if __name__ == "__main__":
    import sys

    # Usage:
    #   python ingest.py          → ingest all (SCL + HMI + CWC)
    #   python ingest.py scl      → ingest SCL only
    #   python ingest.py hmi      → ingest HMI only
    #   python ingest.py cwc      → ingest CWC only
    arg = sys.argv[1].lower() if len(sys.argv) > 1 else "all"

    if arg == "scl":
        ingest_scl_knowledge()
    elif arg == "hmi":
        ingest_hmi_knowledge()
    elif arg == "cwc":
        ingest_cwc_knowledge()
    else:
        ingest_scl_knowledge()
        ingest_hmi_knowledge()
        ingest_cwc_knowledge()
        print("\n🎉 All knowledge bases ingested successfully!")