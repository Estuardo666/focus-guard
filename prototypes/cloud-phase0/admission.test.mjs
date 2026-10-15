import test from 'node:test';
import assert from 'node:assert/strict';
import {admit,PreparationSlot} from './admission.mjs';
const start=Date.parse('2026-10-11T20:00:00.000Z');
const intent={generationId:'generation-test',mode:'Normal',startAtUtc:new Date(start).toISOString(),endAtUtc:new Date(start+60000).toISOString(),normalLateJoinSeconds:600};
const ctx={generationId:intent.generationId,nowMs:start,uncertaintyMs:0,maxRemoteDurationMs:14400000,activeMember:true,targeted:true,alreadyProcessed:false,localBusy:false,normalConsent:true,strictConsent:true,recoveryReady:true,intentDigest:'digest-test',consentRevision:1,mappingRevision:1,ticket:{expiresMs:start+120000,intentDigest:'digest-test',consentRevision:1,mappingRevision:1}};
test('Late normal window can have 35s remaining without restarting duration',()=>assert.deepEqual(admit(intent,{...ctx,nowMs:start+25000}),{outcome:'Admitted',endAtUtc:intent.endAtUtc,remainingMs:35000}));
test('Expired, uncertain, wrong generation, revoked and local busy reject',()=>{for(const [patch,expected]of [[{nowMs:start+60000},'Expired'],[{uncertaintyMs:5001},'ClockUncertain'],[{generationId:'old'},'GenerationMismatch'],[{activeMember:false},'DeviceRevoked'],[{localBusy:true},'SessionAlreadyActive']])assert.equal(admit(intent,{...ctx,...patch}),expected);});
test('Already processed intent cannot restart after normal interruption',()=>assert.equal(admit(intent,{...ctx,alreadyProcessed:true}),'AlreadyProcessed'));
test('Strict requires separate consent, recovery, unexpired bound ticket',()=>{const strict={...intent,mode:'Strict'};for(const [patch,expected]of [[{strictConsent:false},'ConsentRequired'],[{recoveryReady:false},'RecoveryNotReady'],[{ticket:null},'PreparationExpired'],[{ticket:{...ctx.ticket,expiresMs:start}},'PreparationExpired'],[{ticket:{...ctx.ticket,mappingRevision:2}},'PreparationChanged']])assert.equal(admit(strict,{...ctx,...patch}),expected);});
test('Duration and late confirmation limits do not silently change deadline',()=>{assert.equal(admit({...intent,endAtUtc:new Date(start+30000).toISOString()},ctx),'InvalidDuration');const longer={...intent,endAtUtc:new Date(start+3600000).toISOString()};assert.equal(admit(longer,{...ctx,nowMs:start+601000}),'LocalConfirmationRequired');assert.equal(admit(longer,{...ctx,nowMs:start+601000,localConfirmation:true}).endAtUtc,longer.endAtUtc);});
test('Cancel and commit have one winner, cancel after commit cannot unlock',()=>{let s=new PreparationSlot(start+120000,start+3600000);s.ready(start);assert.equal(s.commit(start),'Committed');assert.equal(s.cancel(),'Conflict');assert.equal(s.state,'Committed');s=new PreparationSlot(start+120000,start+3600000);s.ready(start);s.cancel();assert.equal(s.commit(start),'Conflict');});
test('Abandoned prepare and committed-without-ACK slots eventually expire',()=>{const p=new PreparationSlot(start+120000,start+3600000);assert.equal(p.expire(start+120000),'PreparationExpired');const c=new PreparationSlot(start+120000,start+3600000);c.ready(start);c.commit(start);assert.equal(c.expire(start+3600000),'Expired');});
test('Invalid clocks, unknown local status and excessive local duration are rejected',()=>{
 for(const patch of [{nowMs:NaN},{uncertaintyMs:NaN},{uncertaintyMs:-1},{maxRemoteDurationMs:undefined},{localBusy:undefined}])assert.equal(admit(intent,{...ctx,...patch}),'InvalidContext');
 assert.equal(admit(intent,{...ctx,maxRemoteDurationMs:30000}),'ConsentDurationExceeded');
 assert.equal(admit({...intent,mode:'Strict'},{...ctx,ticket:{...ctx.ticket,expiresMs:NaN}}),'PreparationExpired');
});
test('Late ready/commit calls cannot overwrite cancelled, committed or expired state',()=>{
 const cancelled=new PreparationSlot(start+1000,start+60000);cancelled.cancel();assert.equal(cancelled.ready(start+2000),'Cancelled');assert.equal(cancelled.commit(start+2000),'Conflict');assert.equal(cancelled.state,'Cancelled');
 const applied=new PreparationSlot(start+1000,start+60000);applied.ready(start);applied.commit(start);assert.equal(applied.ready(start+2000),'Committed');applied.expire(start+60000);assert.equal(applied.commit(start+60000),'Conflict');assert.equal(applied.state,'Expired');
});
test('Preparation deadline is finite, immutable and bounded by session end',()=>{
 for(const value of [NaN,Infinity,undefined,null]){assert.throws(()=>new PreparationSlot(value,start+60000));assert.throws(()=>new PreparationSlot(start+1000,value));}
 const slot=new PreparationSlot(start+120000,start+60000);assert.equal(slot.expiresMs,start+60000);assert.throws(()=>{slot.endMs=Infinity;});assert.throws(()=>{slot.expiresMs=Infinity;});
});
