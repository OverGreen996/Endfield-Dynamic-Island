import { DatabaseSync } from 'node:sqlite';
import { existsSync, writeFileSync } from 'node:fs';
import { randomUUID } from 'node:crypto';
import { createHash } from 'node:crypto';

export class Locked extends Error {
  constructor(code, status = 429) { super(code); this.code = code; this.status = status; }
}
export const pacificDay = ms => new Intl.DateTimeFormat('en-CA', {
  timeZone: 'America/Los_Angeles', year: 'numeric', month: '2-digit', day: '2-digit'
}).format(new Date(ms));
export function nextReset(ms) {
  const day = pacificDay(ms);
  let lo = ms, hi = ms + 27 * 3600000;
  while (hi - lo > 1) { const mid = Math.floor((lo + hi) / 2); if (pacificDay(mid) === day) lo = mid; else hi = mid; }
  return new Date(hi).toISOString();
}
const positive = n => Number.isSafeInteger(n) && n > 0;
export function freePolicyFingerprint(p) {
  // Bind confirmation to the project, model and actual budgets; no secret is included.
  const v=p.models?.[p.defaultModel];
  return createHash('sha256').update(JSON.stringify([
    p.project,p.defaultModel,p.paidAllowed,p.toolsAllowed,p.dailyRequests,p.dailyTotalTokens,
    p.maxInputTokens,p.maxOutputTokens,p.maxBodyBytes,p.preflightPerMinute,
    Object.keys(p.models??{}).sort(),v?.official?.rpm,v?.official?.tpm,v?.official?.rpd,
    v?.local?.rpm,v?.local?.tpm,v?.local?.rpd,v?.outputReservation
  ])).digest('hex');
}
export function validatePolicy(p) {
  const mode=p?.verification?.mode??'time-limited';
  const pinned=mode==='project-pinned';
  const binding=p?.verification?.binding;
  const verifiedTime=Date.parse(p?.verification?.observedAt);
  const expires=Date.parse(p?.verification?.validUntil);
  if(!['time-limited','project-pinned'].includes(mode) ||
    (pinned ? p.verification.validUntil!==null ||
      !/^[a-f0-9]{64}$/.test(binding?.keySha256??'') ||
      binding?.project!==p.project || binding?.model!==p.defaultModel ||
      binding?.limitsSha256!==freePolicyFingerprint(p) :
      !Number.isFinite(expires) || expires<=verifiedTime || expires-verifiedTime>86400000))
    throw new Locked('invalid_verification_binding',503);
  if (p?.schema !== 1 || p.paidAllowed !== false || p.toolsAllowed !== false ||
      p.verification?.tier !== 'free' || !/^[a-z0-9-]+$/.test(p.project ?? '') ||
      !positive(p.dailyRequests) || !positive(p.dailyTotalTokens) ||
      !positive(p.maxInputTokens) || !positive(p.maxOutputTokens) ||
      !positive(p.maxBodyBytes) || !positive(p.preflightPerMinute) ||
      p.maxInputTokens>32768 || p.maxOutputTokens>2048 || p.maxBodyBytes>131072 ||
      !Number.isFinite(Date.parse(p.verification.observedAt)) ||
      !/^[A-Za-z0-9_-]{4}$/.test(p.verification.keySuffix ?? '') ||
      !p.models || !Object.hasOwn(p.models, p.defaultModel)) throw new Locked('invalid_policy', 503);
  for (const [model, v] of Object.entries(p.models)) {
    const conservative = p.providerLimitsVerified === false;
    if (model !== 'gemini-3.5-flash-lite' ||
        !positive(v.outputReservation) || v.outputReservation < 65536 || v.outputReservation < p.maxOutputTokens ||
        (conservative ? v.official !== null || p.dailyRequests > 20 || v.local?.rpm > 3 || v.local?.tpm > 15000 || v.local?.rpd > 20 ||
          ['rpm','tpm','rpd'].some(k=>!positive(v.local?.[k])) :
          ['rpm', 'tpm', 'rpd'].some(k => !positive(v.official?.[k]) || !positive(v.local?.[k]) || v.local[k] > v.official[k])))
      throw new Locked('invalid_model_policy', 503);
  }
  return structuredClone(p);
}
export class UsageGuard {
  constructor(path, policy, { now = Date.now, initialize = false } = {}) {
    this.p = validatePolicy(policy); this.now = now;
    const memory = path === ':memory:';
    if (!memory && !existsSync(path) && (!initialize || existsSync(path + '.initialized')))
      throw new Locked('usage_database_missing', 503);
    this.db = new DatabaseSync(path);
    try {
    this.db.exec('PRAGMA busy_timeout=5000; PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL;');
    if (memory || initialize && !existsSync(path + '.initialized')) {
      this.db.exec(`
        CREATE TABLE IF NOT EXISTS meta (key TEXT PRIMARY KEY, value TEXT NOT NULL);
        INSERT OR IGNORE INTO meta VALUES ('schema','1'),('last_ms','0');
        CREATE TABLE IF NOT EXISTS events (
          id TEXT PRIMARY KEY, project TEXT NOT NULL, model TEXT NOT NULL,
          ms INTEGER NOT NULL, day TEXT NOT NULL, input INTEGER NOT NULL,
          output INTEGER NOT NULL, prompt INTEGER, candidate INTEGER, thought INTEGER,
          status TEXT NOT NULL, reason TEXT);
        CREATE INDEX IF NOT EXISTS by_time ON events(project,model,ms);
        CREATE INDEX IF NOT EXISTS by_day ON events(project,day);
        CREATE TABLE IF NOT EXISTS locks (project TEXT NOT NULL, model TEXT NOT NULL,
          until_ms INTEGER NOT NULL, reason TEXT NOT NULL, PRIMARY KEY(project,model));
        CREATE TABLE IF NOT EXISTS preflights (project TEXT NOT NULL, ms INTEGER NOT NULL);
      `);
      if (!memory) writeFileSync(path + '.initialized', '1', { flag: 'wx' });
    }
    if (this.db.prepare("SELECT value FROM meta WHERE key='schema'").get()?.value !== '1')
      throw new Locked('usage_database_schema', 503);
    this.db.prepare('SELECT id FROM events LIMIT 1').get();
    this.reconcileProviderFailures();
    } catch(e) { this.db.close(); throw e; }
  }
  close() { this.db.close(); }
  transaction(f) {
    this.db.exec('BEGIN IMMEDIATE');
    try { const r = f(); this.db.exec('COMMIT'); return r; }
    catch (e) { this.db.exec('ROLLBACK'); throw e; }
  }
  time() {
    const ms = this.now();
    const last = Number(this.db.prepare("SELECT value FROM meta WHERE key='last_ms'").get().value);
    if (!Number.isSafeInteger(ms) || ms < last) throw new Locked('clock_rollback', 503);
    this.db.prepare("UPDATE meta SET value=? WHERE key='last_ms'").run(String(ms));
    return ms;
  }
  gate(model, ms) {
    const p = this.p;
    if (!p.enabled) throw new Locked('disabled', 503);
    if (ms < Date.parse(p.verification.observedAt) ||
        (p.verification.mode!=='project-pinned' && ms >= Date.parse(p.verification.validUntil)))
      throw new Locked('free_tier_verification_expired', 503);
    if (!Object.hasOwn(p.models, model)) throw new Locked('model_not_allowed', 400);
    const lock = this.db.prepare('SELECT * FROM locks WHERE project=? AND model=?').get(p.project, model);
    if (lock?.until_ms > ms) throw new Locked(lock.reason);
  }
  totals(model, ms) {
    const project = this.p.project, day = pacificDay(ms);
    const daily = this.db.prepare('SELECT count(*) requests,coalesce(sum(input+output),0) tokens FROM events WHERE project=? AND day=?').get(project,day);
    const md = this.db.prepare('SELECT count(*) requests,coalesce(sum(input+output),0) tokens FROM events WHERE project=? AND model=? AND day=?').get(project,model,day);
    const minute = this.db.prepare('SELECT count(*) requests,coalesce(sum(input),0) input FROM events WHERE project=? AND model=? AND ms>?').get(project,model,ms-60000);
    return {daily,md,minute};
  }
  preflight(model) {
    return this.transaction(() => {
      const ms = this.time(); this.gate(model,ms);
      const {daily,md,minute} = this.totals(model,ms), v=this.p.models[model].local;
      if(daily.requests >= this.p.dailyRequests || md.requests >= v.rpd) throw new Locked('daily_request_limit');
      if(minute.requests >= v.rpm) throw new Locked('minute_request_limit');
      if(daily.tokens + this.p.models[model].outputReservation >= this.p.dailyTotalTokens) throw new Locked('daily_token_limit');
      const count = this.db.prepare('SELECT count(*) n FROM preflights WHERE project=? AND ms>?').get(this.p.project,ms-60000).n;
      if(count >= this.p.preflightPerMinute) throw new Locked('preflight_rate_limit');
      this.db.prepare('DELETE FROM preflights WHERE ms<=?').run(ms-60000);
      this.db.prepare('INSERT INTO preflights VALUES (?,?)').run(this.p.project,ms);
    });
  }
  reserve(model, countedInput) {
    if(!positive(countedInput)) throw new Locked('invalid_token_count',503);
    // countTokens is a preflight; include 256 tokens of headroom for generation overhead.
    const input = countedInput + 256;
    return this.transaction(() => {
      const ms = this.time(); this.gate(model,ms);
      const {daily,md,minute}=this.totals(model,ms), p=this.p, v=p.models[model], output=v.outputReservation;
      if(input > p.maxInputTokens) throw new Locked('input_token_limit',413);
      if(daily.requests+1>p.dailyRequests || md.requests+1>v.local.rpd) throw new Locked('daily_request_limit');
      if(minute.requests+1>v.local.rpm) throw new Locked('minute_request_limit');
      if(minute.input+input>v.local.tpm) throw new Locked('minute_input_token_limit');
      if(daily.tokens+input+output>p.dailyTotalTokens) throw new Locked('daily_token_limit');
      const id=randomUUID();
      this.db.prepare('INSERT INTO events(id,project,model,ms,day,input,output,status) VALUES (?,?,?,?,?,?,?,?)')
        .run(id,p.project,model,ms,pacificDay(ms),input,output,'reserved');
      return id;
    });
  }
  lock(model, reason, until) {
    this.db.prepare('INSERT INTO locks VALUES (?,?,?,?) ON CONFLICT(project,model) DO UPDATE SET until_ms=max(until_ms,excluded.until_ms),reason=excluded.reason')
      .run(this.p.project,model,until,reason);
  }
  finish(id, usage) {
    return this.transaction(() => {
      const e=this.db.prepare('SELECT * FROM events WHERE id=? AND project=?').get(id,this.p.project);
      if(!e || e.status!=='reserved') throw new Locked('reservation_state',503);
      const ms=this.time();
      const valid=n=>Number.isSafeInteger(n)&&n>=0;
      const prompt=usage?.promptTokenCount, candidate=usage?.candidatesTokenCount??0, thought=usage?.thoughtsTokenCount??0, total=usage?.totalTokenCount;
      if(!valid(prompt)||!valid(candidate)||!valid(thought)||!valid(total)||total<prompt+candidate+thought ||
          (usage.toolUsePromptTokenCount??0)!==0) {
        this.db.prepare("UPDATE events SET status='uncertain',reason='invalid_usage_metadata' WHERE id=?").run(id);
        this.lock(e.model,'unknown_usage_lock',Date.parse(nextReset(ms))); return false;
      }
      const output=Math.max(candidate+thought,total-prompt);
      // Keep input preflight headroom for rolling TPM; release unused output reservation only.
      this.db.prepare("UPDATE events SET input=?,output=?,prompt=?,candidate=?,thought=?,status='finished' WHERE id=?")
        .run(Math.max(e.input,prompt),output,prompt,candidate,thought,id);
      if(prompt>e.input || output>e.output) this.lock(e.model,'reservation_exceeded_lock',Date.parse(nextReset(ms)));
      return true;
    });
  }
  fail(id, status=0) {
    return this.transaction(() => {
      const e=this.db.prepare("SELECT * FROM events WHERE id=? AND project=? AND status='reserved'").get(id,this.p.project);
      if(!e) return;
      const ms=this.time();
      // Google confirms failed HTTP 500/503 responses consume request quota but
      // not billable tokens. Retain the full token reservation conservatively;
      // a received HTTP error differs from an unknown/cancelled transport.
      const temporary=status===500||status===503;
      this.db.prepare('UPDATE events SET status=?,reason=? WHERE id=?')
        .run(temporary?'failed':'uncertain',status?'http_'+status:'transport_unknown',id);
      const existing=this.db.prepare('SELECT * FROM locks WHERE project=? AND model=?').get(this.p.project,e.model);
      if(!temporary||!existing||existing.until_ms<=ms||existing.reason==='provider_temporary_unavailable')
        this.lock(e.model,temporary?'provider_temporary_unavailable':status===429?'provider_429_lock':'unknown_usage_lock',
          temporary?ms+60000:Date.parse(nextReset(ms)));
    });
  }
  reconcileProviderFailures() {
    // Repair the previous version's recorded, received HTTP 500/503 failures.
    // Never reset counters or clear locks caused by unknown transport/usage,
    // 429, reservation overrun, or an unresolved in-flight request.
    return this.transaction(()=>{
      const ms=this.time(),day=pacificDay(ms);
      const rows=this.db.prepare("SELECT * FROM events WHERE project=? AND day=? AND status='uncertain' AND reason IN ('http_500','http_503')")
        .all(this.p.project,day);
      for(const e of rows)this.db.prepare("UPDATE events SET status='failed' WHERE id=?").run(e.id);
      for(const model of new Set(rows.map(e=>e.model))){
        const lock=this.db.prepare('SELECT * FROM locks WHERE project=? AND model=?').get(this.p.project,model);
        const unknown=this.db.prepare("SELECT count(*) n FROM events WHERE project=? AND model=? AND day=? AND status IN ('reserved','uncertain')")
          .get(this.p.project,model,day).n;
        if(lock?.reason!=='unknown_usage_lock'||unknown)continue;
        const until=Math.max(...rows.filter(e=>e.model===model).map(e=>e.ms+60000));
        if(until>ms)this.db.prepare("UPDATE locks SET until_ms=?,reason='provider_temporary_unavailable' WHERE project=? AND model=?")
          .run(until,this.p.project,model);
        else this.db.prepare('DELETE FROM locks WHERE project=? AND model=?').run(this.p.project,model);
      }
      return rows.length;
    });
  }
  preflightFailure(model, status) {
    if(status!==429) return;
    this.transaction(()=>{const ms=this.time();this.lock(model,'provider_429_lock',Date.parse(nextReset(ms)));});
  }
  status(keyPresent=false) {
    return this.transaction(() => {
      const ms=this.time(), p=this.p, models={};
      for(const [model,v] of Object.entries(p.models)) {
        let reason=null;
        try{this.gate(model,ms);}catch(e){reason=e.code??'guard_error';}
        const t=this.totals(model,ms);
        const reported=this.db.prepare('SELECT coalesce(sum(prompt),0) input,coalesce(sum(candidate),0) output,coalesce(sum(thought),0) thinking FROM events WHERE project=? AND model=? AND day=?')
          .get(p.project,model,pacificDay(ms));
        models[model]={official_snapshot:v.official,local_limits:v.local,
          used_requests_today:t.md.requests,used_requests_last_minute:t.minute.requests,
          remaining_requests_today:Math.max(0,v.local.rpd-t.md.requests),
          remaining_requests_last_minute:Math.max(0,v.local.rpm-t.minute.requests),
          input_tokens_last_minute:t.minute.input,accounted_tokens_today:t.md.tokens,
          reported_tokens_today:reported,
          locked_reason:reason,callable:!reason&&keyPresent&&t.md.requests<v.local.rpd&&t.minute.requests<v.local.rpm&&
            t.daily.requests<p.dailyRequests&&t.daily.tokens+v.outputReservation+256<p.dailyTotalTokens};
      }
      const daily=this.totals(p.defaultModel,ms).daily;
      const uncertain=this.db.prepare("SELECT count(*) n FROM events WHERE project=? AND day=? AND status IN ('reserved','uncertain')").get(p.project,pacificDay(ms)).n;
      return {project:p.project,scope:'local_hub_only',google_live_remaining:null,provider_usage_delay_minutes:15,
        default_model:p.defaultModel,free_tier_verified_until:p.verification.validUntil,key_present:keyPresent,
        free_tier_verification_mode:p.verification.mode??'time-limited',
        provider_limits_verified:p.providerLimitsVerified!==false,
        free_tier_confirmed_at:p.verification.observedAt,
        billing_status_source:'local_confirmation',billing_status_live_verified:false,
        paid_allowed:false,search_tools_allowed:false,day_timezone:'America/Los_Angeles',next_daily_reset:nextReset(ms),
        daily_local_request_limit:p.dailyRequests,daily_local_token_limit:p.dailyTotalTokens,
        accounted_tokens_today:daily.tokens,remaining_local_tokens_today:Math.max(0,p.dailyTotalTokens-daily.tokens),
        unknown_or_inflight_requests:uncertain,models};
    });
  }
}
