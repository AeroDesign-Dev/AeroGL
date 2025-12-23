using System.Data.SQLite;

namespace AeroGL.Data
{
    /// <summary>
    /// Membuat semua tabel, index, dan trigger bila belum ada.
    /// Panggil sekali saat startup: AeroGL.Data.Schema.Init();
    /// </summary>
    public static class Schema
    {
        public static void Init()
        {
            using (var cn = Db.Open())
            using (var cmd = cn.CreateCommand())
            {
                cmd.CommandText = @"
                    PRAGMA foreign_keys = ON;

                    -- 1) Coa (master akun 3-seg)
                    CREATE TABLE IF NOT EXISTS Coa(
                      Code3 TEXT PRIMARY KEY,
                      Name  TEXT NOT NULL,
                      Type  INTEGER NOT NULL CHECK (Type IN (0,1)),
                      Grp   INTEGER NOT NULL CHECK (Grp IN (1,2,3,4,5))
                    );

                    -- 2) CoaBalance (ember per bulan)
                    CREATE TABLE IF NOT EXISTS CoaBalance(
                      Code3  TEXT NOT NULL,
                      Year   INTEGER NOT NULL,
                      Month  INTEGER NOT NULL CHECK (Month BETWEEN 0 AND 12),
                      Saldo  NUMERIC NOT NULL DEFAULT 0,
                      Debet  NUMERIC NOT NULL DEFAULT 0,
                      Kredit NUMERIC NOT NULL DEFAULT 0,
                      PRIMARY KEY(Code3,Year,Month),
                      FOREIGN KEY(Code3) REFERENCES Coa(Code3)
                    );
                    CREATE INDEX IF NOT EXISTS IX_CoaBalance_Code3YM ON CoaBalance(Code3,Year,Month);

                    -- 3) JournalHeader (dengan tipe J/M dan cache total)
                    CREATE TABLE IF NOT EXISTS JournalHeader(
                      NoTran      TEXT PRIMARY KEY,
                      Tanggal     TEXT NOT NULL,                     -- yyyy-MM-dd
                      Memo        TEXT,
                      Type        TEXT NOT NULL DEFAULT 'J' CHECK (Type IN ('J','M')),
                      TotalDebet  NUMERIC NOT NULL DEFAULT 0,
                      TotalKredit NUMERIC NOT NULL DEFAULT 0
                    );

                    -- 4) JournalLine (detail 2-seg)
                    CREATE TABLE IF NOT EXISTS JournalLine(
                      NoTran    TEXT NOT NULL,
                      Code2     TEXT NOT NULL,                       -- xxx.xxx
                      Side      TEXT NOT NULL CHECK (Side IN ('D','K')),
                      Amount    NUMERIC NOT NULL,
                      Narration TEXT,
                      FOREIGN KEY(NoTran) REFERENCES JournalHeader(NoTran)
                    );
                    CREATE INDEX IF NOT EXISTS IX_JournalLine_NoTran ON JournalLine(NoTran);

                    -- 5) Alias2To3 (mapping 2-seg -> 3-seg)
                    CREATE TABLE IF NOT EXISTS Alias2To3(
                      Code2 TEXT PRIMARY KEY,
                      Code3 TEXT NOT NULL
                    );

                    -- 6) Control (periode aktif)
                    CREATE TABLE IF NOT EXISTS Control(
                      Year INTEGER,
                      Month INTEGER,
                      LastClose TEXT
                    );

                    -- === TRIGGERS: sinkronkan TotalDebet/TotalKredit di header ===
                    CREATE TRIGGER IF NOT EXISTS trg_line_ins AFTER INSERT ON JournalLine
                    BEGIN
                      UPDATE JournalHeader
                      SET TotalDebet  = TotalDebet  + (CASE WHEN NEW.Side='D' THEN NEW.Amount ELSE 0 END),
                          TotalKredit = TotalKredit + (CASE WHEN NEW.Side='K' THEN NEW.Amount ELSE 0 END)
                      WHERE NoTran = NEW.NoTran;
                    END;

                    CREATE TRIGGER IF NOT EXISTS trg_line_upd AFTER UPDATE ON JournalLine
                    BEGIN
                      UPDATE JournalHeader
                      SET TotalDebet  = TotalDebet
                                        - (CASE WHEN OLD.Side='D' THEN OLD.Amount ELSE 0 END)
                                        + (CASE WHEN NEW.Side='D' THEN NEW.Amount ELSE 0 END),
                          TotalKredit = TotalKredit
                                        - (CASE WHEN OLD.Side='K' THEN OLD.Amount ELSE 0 END)
                                        + (CASE WHEN NEW.Side='K' THEN NEW.Amount ELSE 0 END)
                      WHERE NoTran = NEW.NoTran;
                    END;

                    CREATE TRIGGER IF NOT EXISTS trg_line_del AFTER DELETE ON JournalLine
                    BEGIN
                      UPDATE JournalHeader
                      SET TotalDebet  = TotalDebet  - (CASE WHEN OLD.Side='D' THEN OLD.Amount ELSE 0 END),
                          TotalKredit = TotalKredit - (CASE WHEN OLD.Side='K' THEN OLD.Amount ELSE 0 END)
                      WHERE NoTran = OLD.NoTran;
                    END;
                    ";

                cmd.CommandText += @"
                    -- 7) Config (Tabel Setting)
                    CREATE TABLE IF NOT EXISTS Config (
                        Key TEXT PRIMARY KEY,
                        Val TEXT
                    );

                    -- Default Value (AMAN: Gak akan nimpah kalau user udah ganti)
                    INSERT OR IGNORE INTO Config(Key, Val) VALUES ('LabaDitahan', '016');
                    INSERT OR IGNORE INTO Config(Key, Val) VALUES ('LabaBerjalan', '017');
                ";

                cmd.ExecuteNonQuery();
            }
        }
    }
}
