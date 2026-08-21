import fs from "node:fs";
import os from "node:os";
import path from "node:path";

type SqliteRow = Record<string, unknown>;

type NodeSqliteDatabase = {
  prepare(sql: string): { all: (...params: unknown[]) => SqliteRow[] };
  close(): void;
};

type NodeSqliteModule = {
  DatabaseSync: new (file: string, options?: { readOnly?: boolean }) => NodeSqliteDatabase;
};

let nodeSqlite: NodeSqliteModule | null | undefined;

function silenceSqliteExperimentalWarning<T>(run: () => T): T {
  const original = process.emitWarning;
  process.emitWarning = ((warning: unknown, ...args: unknown[]) => {
    const text = typeof warning === "string"
      ? warning
      : warning instanceof Error
        ? warning.message
        : "";
    if (text.includes("SQLite is an experimental feature")) {
      return;
    }
    return (original as (...inner: unknown[]) => void).call(process, warning, ...args);
  }) as typeof process.emitWarning;
  try {
    return run();
  } finally {
    process.emitWarning = original;
  }
}

async function loadNodeSqlite(): Promise<NodeSqliteModule | null> {
  if (nodeSqlite !== undefined) {
    return nodeSqlite;
  }
  const original = process.emitWarning;
  process.emitWarning = ((warning: unknown, ...args: unknown[]) => {
    const text = typeof warning === "string"
      ? warning
      : warning instanceof Error
        ? warning.message
        : "";
    if (text.includes("SQLite is an experimental feature")) {
      return;
    }
    return (original as (...inner: unknown[]) => void).call(process, warning, ...args);
  }) as typeof process.emitWarning;
  try {
    nodeSqlite = (await import("node:sqlite")) as NodeSqliteModule;
  } catch {
    nodeSqlite = null;
  } finally {
    process.emitWarning = original;
  }
  return nodeSqlite;
}

export function copySqliteToTemp(sourcePath: string): { directory: string; filePath: string } {
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), "mytools-browser-"));
  const filePath = path.join(directory, "db.sqlite");
  // Copy only the main file. Mixing a live WAL/SHM with a copied DB makes SQLite
  // apply someone else's journal and yields a corrupt snapshot.
  fs.copyFileSync(sourcePath, filePath);
  return { directory, filePath };
}

export function removeTempDir(directory: string): void {
  try {
    fs.rmSync(directory, { recursive: true, force: true });
  } catch {
    // Ignore cleanup failures in temp.
  }
}

function queryAll(db: NodeSqliteDatabase, sql: string): SqliteRow[] {
  const statement = db.prepare(sql) as {
    all?: (...params: unknown[]) => SqliteRow[];
    iterate?: () => Iterable<SqliteRow>;
    setReadBigInts?: (enabled: boolean) => void;
  };
  statement.setReadBigInts?.(true);
  if (typeof statement.all === "function") {
    return statement.all();
  }
  if (typeof statement.iterate === "function") {
    return [...statement.iterate()];
  }
  throw new Error("node:sqlite statement does not support all/iterate");
}

export async function readSqliteQuery(filePath: string, sql: string): Promise<SqliteRow[]> {
  const sqlite = await loadNodeSqlite();
  if (!sqlite?.DatabaseSync) {
    return [];
  }

  const copy = copySqliteToTemp(filePath);
  try {
    return silenceSqliteExperimentalWarning(() => {
      let db: NodeSqliteDatabase;
      try {
        db = new sqlite.DatabaseSync(copy.filePath, { readOnly: true });
      } catch {
        db = new sqlite.DatabaseSync(copy.filePath);
      }
      try {
        return queryAll(db, sql);
      } finally {
        db.close();
      }
    });
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error);
    console.error(`[browser-search] sqlite read failed for ${filePath}: ${message}`);
    return [];
  } finally {
    removeTempDir(copy.directory);
  }
}

export async function readSqliteTable(filePath: string, tableName: string): Promise<SqliteRow[]> {
  const quoted = tableName.replaceAll("\"", "\"\"");
  return readSqliteQuery(filePath, `SELECT * FROM "${quoted}"`);
}

export function readSqliteTableFromBuffer(buffer: Buffer, tableName: string): SqliteRow[] {
  if (buffer.length < 100 || buffer.subarray(0, 16).toString("utf8") !== "SQLite format 3\0") {
    return [];
  }
  const encoding = buffer.readUInt32BE(56);
  if (encoding !== 1) {
    return [];
  }
  const pageSizeRaw = buffer.readUInt16BE(16);
  const pageSize = pageSizeRaw === 1 ? 65536 : pageSizeRaw;
  const reserved = buffer[20];
  const usable = pageSize - reserved;
  const db = { buffer, pageSize, usable };

  const masterRows = readTablePage(db, 1, true).map(mapSqliteMasterRow);
  const table = masterRows.find((row) => {
    const type = String(row.type || "");
    const name = String(row.name || "");
    const tbl = String(row.tbl_name || "");
    return type === "table" && (name === tableName || tbl === tableName);
  });
  if (!table) {
    return [];
  }
  const rootPage = Number(table.rootpage);
  if (!Number.isFinite(rootPage) || rootPage < 1) {
    return [];
  }
  const columns = parseCreateTableColumns(String(table.sql || ""), tableName);
  const records = readTablePage(db, rootPage, false);
  if (columns.length === 0) {
    return records;
  }
  return records.map((row) => mapDataRow(row, columns));
}

type ColumnDef = {
  name: string;
  isRowId: boolean;
};

function mapSqliteMasterRow(row: SqliteRow): SqliteRow {
  return {
    type: rowText(row, "type", "0", "c0"),
    name: rowText(row, "name", "1", "c1"),
    tbl_name: rowText(row, "tbl_name", "2", "c2"),
    rootpage: rowNumber(row, "rootpage", "3", "c3"),
    sql: rowText(row, "sql", "4", "c4"),
  };
}

function mapDataRow(row: SqliteRow, columns: ColumnDef[]): SqliteRow {
  const mapped: SqliteRow = {};
  for (let i = 0; i < columns.length; i++) {
    const column = columns[i];
    const payload = row[String(i)] ?? row[`c${i}`];
    if (column.isRowId) {
      mapped[column.name] = payload ?? row.rowid ?? row.id;
    } else {
      mapped[column.name] = payload;
    }
  }
  return mapped;
}

function parseCreateTableColumns(sql: string, tableName: string): ColumnDef[] {
  const match = sql.match(new RegExp(`create\\s+table\\s+(?:\"?${escapeRegExp(tableName)}\"?\\s*)?\\(([\\s\\S]+)\\)`, "i"));
  if (!match) {
    return [];
  }
  return match[1]
    .split(",")
    .map((part) => part.trim())
    .filter((part) => part.length > 0 && !/^(constraint|primary|unique|check|foreign)\b/i.test(part))
    .map((part) => {
      const name = part.replace(/^["[]?([^"\]\s]+)["\]]?.*/, "$1");
      return {
        name,
        isRowId: /integer\s+primary\s+key/i.test(part),
      };
    });
}

function escapeRegExp(value: string): string {
  return value.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
}

type SqliteDb = {
  buffer: Buffer;
  pageSize: number;
  usable: number;
};

function readTablePage(db: SqliteDb, pageNumber: number, isPage1: boolean): SqliteRow[] {
  const page = loadPage(db, pageNumber);
  if (!page) {
    return [];
  }
  const headerOffset = isPage1 && pageNumber === 1 ? 100 : 0;
  const type = page[headerOffset];
  if (type === 0x05) {
    return readInteriorTable(db, page, headerOffset, pageNumber === 1);
  }
  if (type === 0x0d) {
    return readLeafTable(db, page, headerOffset);
  }
  return [];
}

function loadPage(db: SqliteDb, pageNumber: number): Buffer | null {
  const start = (pageNumber - 1) * db.pageSize;
  if (start < 0 || start + db.pageSize > db.buffer.length) {
    return null;
  }
  return db.buffer.subarray(start, start + db.pageSize);
}

function readInteriorTable(db: SqliteDb, page: Buffer, headerOffset: number, isPage1: boolean): SqliteRow[] {
  const cellCount = page.readUInt16BE(headerOffset + 3);
  const rightMost = page.readUInt32BE(headerOffset + 8);
  const pointerStart = headerOffset + 12;
  const rows: SqliteRow[] = [];
  for (let i = 0; i < cellCount; i++) {
    const cellOffset = page.readUInt16BE(pointerStart + i * 2);
    if (cellOffset + 4 > page.length) {
      continue;
    }
    const childPage = page.readUInt32BE(cellOffset);
    rows.push(...readTablePage(db, childPage, isPage1 && childPage === 1));
  }
  rows.push(...readTablePage(db, rightMost, isPage1 && rightMost === 1));
  return rows;
}

function readLeafTable(db: SqliteDb, page: Buffer, headerOffset: number): SqliteRow[] {
  const cellCount = page.readUInt16BE(headerOffset + 3);
  const pointerStart = headerOffset + 8;
  const rows: SqliteRow[] = [];
  for (let i = 0; i < cellCount; i++) {
    const cellOffset = page.readUInt16BE(pointerStart + i * 2);
    const row = readLeafCell(db, page, cellOffset);
    if (row) {
      rows.push(row);
    }
  }
  return rows;
}

function readLeafCell(db: SqliteDb, page: Buffer, cellOffset: number): SqliteRow | null {
  if (cellOffset < 0 || cellOffset >= page.length) {
    return null;
  }
  let offset = cellOffset;
  const payloadSize = readVarint(page, offset);
  offset += payloadSize.size;
  const rowid = readVarint(page, offset);
  offset += rowid.size;
  const local = localPayloadSize(payloadSize.value, db.usable);
  const localBytes = page.subarray(offset, offset + local.localSize);
  const payload = local.overflow
    ? concatOverflow(db, localBytes, page.readUInt32BE(offset + local.localSize - 4), local.localSize - 4)
    : Buffer.from(localBytes);
  return decodeRecord(payload, rowid.value);
}

function localPayloadSize(payloadSize: number, usable: number): { localSize: number; overflow: boolean } {
  const maxLocal = usable - 35;
  if (payloadSize <= maxLocal) {
    return { localSize: payloadSize, overflow: false };
  }
  const minLocal = Math.floor(((usable - 12) * 32 / 255) - 23);
  const local = minLocal + ((payloadSize - minLocal) % (usable - 4));
  const localSize = Math.min(local, maxLocal);
  return { localSize: localSize + 4, overflow: true };
}

function concatOverflow(db: SqliteDb, first: Buffer, firstOverflowPage: number, keep: number): Buffer {
  const chunks = [first.subarray(0, keep)];
  let pageNumber = firstOverflowPage;
  while (pageNumber !== 0) {
    const page = loadPage(db, pageNumber);
    if (!page) {
      break;
    }
    pageNumber = page.readUInt32BE(0);
    chunks.push(page.subarray(4, db.usable));
  }
  return Buffer.concat(chunks);
}

function decodeRecord(payload: Buffer, rowid: number): SqliteRow {
  let offset = 0;
  const headerSize = readVarint(payload, offset);
  offset += headerSize.size;
  const headerEnd = headerSize.value;
  const serialTypes: number[] = [];
  while (offset < headerEnd && offset < payload.length) {
    const serial = readVarint(payload, offset);
    offset += serial.size;
    serialTypes.push(serial.value);
  }
  let dataOffset = headerEnd;
  const row: SqliteRow = { id: rowid, rowid };
  for (let i = 0; i < serialTypes.length; i++) {
    const decoded = decodeValue(payload, dataOffset, serialTypes[i]);
    dataOffset += decoded.size;
    row[String(i)] = decoded.value;
    row[`c${i}`] = decoded.value;
    if (i === 0 && row.id == null) {
      row.id = decoded.value;
    }
  }
  return row;
}

function decodeValue(buffer: Buffer, offset: number, serialType: number): { value: unknown; size: number } {
  if (serialType === 0) {
    return { value: null, size: 0 };
  }
  if (serialType === 1) {
    return { value: buffer.readInt8(offset), size: 1 };
  }
  if (serialType === 2) {
    return { value: buffer.readInt16BE(offset), size: 2 };
  }
  if (serialType === 3) {
    return { value: (buffer[offset] << 16) | (buffer[offset + 1] << 8) | buffer[offset + 2], size: 3 };
  }
  if (serialType === 4) {
    return { value: buffer.readInt32BE(offset), size: 4 };
  }
  if (serialType === 5) {
    const hi = buffer.readUInt16BE(offset);
    const lo = buffer.readUInt32BE(offset + 2);
    return { value: hi * 2 ** 32 + lo, size: 6 };
  }
  if (serialType === 6) {
    return { value: Number(buffer.readBigInt64BE(offset)), size: 8 };
  }
  if (serialType === 7) {
    return { value: buffer.readDoubleBE(offset), size: 8 };
  }
  if (serialType === 8) {
    return { value: 0, size: 0 };
  }
  if (serialType === 9) {
    return { value: 1, size: 0 };
  }
  if (serialType >= 12 && serialType % 2 === 0) {
    const size = (serialType - 12) / 2;
    return { value: buffer.subarray(offset, offset + size), size };
  }
  if (serialType >= 13 && serialType % 2 === 1) {
    const size = (serialType - 13) / 2;
    return { value: buffer.subarray(offset, offset + size).toString("utf8"), size };
  }
  return { value: null, size: 0 };
}

function readVarint(buffer: Buffer, offset: number): { value: number; size: number } {
  let value = 0;
  for (let i = 0; i < 8; i++) {
    if (offset + i >= buffer.length) {
      return { value, size: i };
    }
    const byte = buffer[offset + i];
    value = (value << 7) | (byte & 0x7f);
    if ((byte & 0x80) === 0) {
      return { value, size: i + 1 };
    }
  }
  if (offset + 8 >= buffer.length) {
    return { value, size: 8 };
  }
  value = (value << 8) | buffer[offset + 8];
  return { value, size: 9 };
}

function columnValue(row: SqliteRow, names: string[]): unknown {
  for (const name of names) {
    if (row[name] !== undefined) {
      return row[name];
    }
    const lower = name.toLowerCase();
    for (const [key, value] of Object.entries(row)) {
      if (key.toLowerCase() === lower) {
        return value;
      }
    }
  }
  return undefined;
}

export function rowText(row: SqliteRow, ...names: string[]): string {
  const value = columnValue(row, names);
  return value == null ? "" : String(value);
}

export function rowNumber(row: SqliteRow, ...names: string[]): number {
  const value = columnValue(row, names);
  if (typeof value === "bigint") {
    const number = Number(value);
    return Number.isFinite(number) ? number : 0;
  }
  const number = typeof value === "number" ? value : Number(value);
  return Number.isFinite(number) ? number : 0;
}
