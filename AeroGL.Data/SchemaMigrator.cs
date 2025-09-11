// AeroGL.Data/SchemaMigrator.cs
using System.Data.SQLite;

namespace AeroGL.Data
{
    public static class SchemaMigrator
    {
        public static void MigrateV2()
        {
            using (var cn = Db.Open())
            using (var cmd = cn.CreateCommand())
            {
                cmd.CommandText = @"
PRAGMA foreign_keys = ON;
BEGIN TRANSACTION;

CREATE TABLE IF NOT EXISTS JournalLine_new(
  NoTran    TEXT NOT NULL,
  Code2     TEXT NOT NULL,
  Side      TEXT NOT NULL CHECK (Side IN ('D','K')),
  Amount    NUMERIC NOT NULL,
  Narration TEXT,
  FOREIGN KEY(NoTran) REFERENCES JournalHeader(NoTran) ON UPDATE CASCADE
);

INSERT INTO JournalLine_new(NoTran, Code2, Side, Amount, Narration)
SELECT NoTran, Code2, Side, Amount, Narration FROM JournalLine;

DROP TABLE JournalLine;
ALTER TABLE JournalLine_new RENAME TO JournalLine;

CREATE INDEX IF NOT EXISTS IX_JournalLine_NoTran ON JournalLine(NoTran);

DROP TRIGGER IF EXISTS trg_line_ins;
CREATE TRIGGER trg_line_ins AFTER INSERT ON JournalLine
BEGIN
  UPDATE JournalHeader
  SET TotalDebet  = TotalDebet  + (CASE WHEN NEW.Side='D' THEN NEW.Amount ELSE 0 END),
      TotalKredit = TotalKredit + (CASE WHEN NEW.Side='K' THEN NEW.Amount ELSE 0 END)
  WHERE NoTran = NEW.NoTran;
END;

DROP TRIGGER IF EXISTS trg_line_upd;
CREATE TRIGGER trg_line_upd AFTER UPDATE ON JournalLine
BEGIN
  UPDATE JournalHeader
  SET TotalDebet  = TotalDebet  - (CASE WHEN OLD.Side='D' THEN OLD.Amount ELSE 0 END),
      TotalKredit = TotalKredit - (CASE WHEN OLD.Side='K' THEN OLD.Amount ELSE 0 END)
  WHERE NoTran = OLD.NoTran;

  UPDATE JournalHeader
  SET TotalDebet  = TotalDebet  + (CASE WHEN NEW.Side='D' THEN NEW.Amount ELSE 0 END),
      TotalKredit = TotalKredit + (CASE WHEN NEW.Side='K' THEN NEW.Amount ELSE 0 END)
  WHERE NoTran = NEW.NoTran;
END;

DROP TRIGGER IF EXISTS trg_line_del;
CREATE TRIGGER trg_line_del AFTER DELETE ON JournalLine
BEGIN
  UPDATE JournalHeader
  SET TotalDebet  = TotalDebet  - (CASE WHEN OLD.Side='D' THEN OLD.Amount ELSE 0 END),
      TotalKredit = TotalKredit - (CASE WHEN OLD.Side='K' THEN OLD.Amount ELSE 0 END)
  WHERE NoTran = OLD.NoTran;
END;

DROP TRIGGER IF EXISTS trg_forbid_line_move;
CREATE TRIGGER trg_forbid_line_move
BEFORE UPDATE OF NoTran ON JournalLine
WHEN NEW.NoTran <> OLD.NoTran
BEGIN
  SELECT RAISE(ABORT, 'Tidak boleh memindahkan satu baris ke header lain. Ubah NoTran di header supaya semua baris ikut.');
END;

COMMIT;

BEGIN TRANSACTION;

UPDATE JournalHeader SET TotalDebet = 0, TotalKredit = 0;

WITH agg AS (
  SELECT NoTran,
         SUM(CASE WHEN Side='D' THEN Amount ELSE 0 END) AS D,
         SUM(CASE WHEN Side='K' THEN Amount ELSE 0 END) AS K
  FROM JournalLine
  GROUP BY NoTran
)
UPDATE JournalHeader
SET TotalDebet  = COALESCE(agg.D,0),
    TotalKredit = COALESCE(agg.K,0)
FROM agg
WHERE JournalHeader.NoTran = agg.NoTran;

COMMIT;
";
                cmd.ExecuteNonQuery();
            }
        }
    }
}
