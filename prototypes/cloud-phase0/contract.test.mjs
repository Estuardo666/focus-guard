import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import canonicalize from 'canonicalize';
import SwaggerParser from '@apidevtools/swagger-parser';
import {readContract,validators} from './contracts.mjs';
const id='11111111-1111-4111-8111-111111111111';
const digest='A'.repeat(43), signature='A'.repeat(86);
const intent={protocol:'FG-INTENT-1',accountId:id,generationId:id,commandId:id,sessionId:id,originDeviceId:id,targetDeviceIds:[id],membershipRevision:1,keyEpoch:1,issuedAtUtc:'2026-10-11T20:00:00.000Z',startAtUtc:'2026-10-11T20:00:00.000Z',endAtUtc:'2026-10-11T20:01:00.000Z',mode:'Normal',normalLateJoinSeconds:600,snapshot:{entityId:id,revision:1,digest},originSignature:signature};
const {get}=validators();
test('OpenAPI 3.1 passes complete structural validator without remote references',async()=>{await SwaggerParser.validate(readContract(),{resolve:{external:false}});});
test('Intent accepts documented shape',()=>assert.equal(get('Intent')(intent),true));
test('Normal and strict preparation schemas reject crossed modes and strict automatic late joining',()=>{
 const strict={...intent,mode:'Strict',normalLateJoinSeconds:0};
 assert(get('StartNormalRequest')({intent}));
 assert.equal(get('StartNormalRequest')({intent:strict}),false);
 assert(get('PrepareStrictRequest')({intent:strict}));
 assert.equal(get('PrepareStrictRequest')({intent}),false);
 assert.equal(get('PrepareStrictRequest')({intent:{...strict,normalLateJoinSeconds:600}}),false);
});
test('Intent rejects unknown fields, wrong mode, unsafe revision, zero targets and extra target duplicates',()=>{for(const mutation of [{...intent,arbitraryCommand:'cmd.exe'},{...intent,mode:'Admin'},{...intent,membershipRevision:2**53},{...intent,targetDeviceIds:[]},{...intent,targetDeviceIds:[id,id]}])assert.equal(get('Intent')(mutation),false,JSON.stringify(mutation));});
test('Intent rejects malformed IDs, signature and snapshot',()=>{for(const mutation of [{...intent,accountId:'other-account'},{...intent,originSignature:'short'},{...intent,snapshot:{entityId:id,revision:1,digest,privateRules:[]}}])assert.equal(get('Intent')(mutation),false);});
test('Operational routes require proof and device headers; bootstrap pending reader has applicant proof',()=>{
 const api=readContract();for(const [path,entry]of Object.entries(api.paths)){if(path.startsWith('/v1/bootstrap/'))continue;for(const operation of Object.values(entry)){assert.deepEqual(operation.security??api.security,[{oidc:[],deviceProof:[]}]);for(const header of ['X-FG-Device-Id','X-FG-Generation-Id','X-FG-Request-Id','X-FG-Timestamp','X-FG-Device-Session'])assert(operation.parameters.some(p=>p.name===header&&p.required),path+' '+header);}}
 assert(api.paths['/v1/bootstrap/pairings/{pairingId}'].get.parameters.some(p=>p.name==='X-FG-Pending-Proof'&&p.required));
});
test('Android pairing completion, membership refresh and session renewal match the signed API contract',()=>{
 const api=readContract(),ref=name=>`#/components/schemas/${name}`;
 const request=api.paths['/v1/bootstrap/pairings'].post;
 assert.deepEqual(request.parameters??[],[]);
 assert.equal(request.requestBody.content['application/json'].schema.$ref,ref('PairingRequest'));
 const ownPairing=api.paths['/v1/bootstrap/pairings/{pairingId}'].get;
 assert.equal(ownPairing.responses['200'].content['application/json'].schema.$ref,ref('PairingState'));
 const state=api.components.schemas.PairingState;
 for(const field of ['membershipChain','currentMembership','currentMembershipDigest','currentKeyEnvelopes','currentMembershipChain'])assert(state.required.includes(field));
 assert.equal(state.additionalProperties,false);
 const confirm=api.paths['/v1/bootstrap/pairings/{pairingId}/confirm'].post;
 assert(confirm.parameters.some(p=>p.name==='X-FG-Pending-Proof'&&p.required));
 assert.deepEqual(confirm.requestBody.content['application/json'].schema,{$ref:ref('PairingConfirmation')});
 assert.equal(confirm.responses['200'].content['application/json'].schema.$ref,ref('PairingConfirmResult'));
 const membership=api.paths['/v1/bootstrap/membership'].get;
 for(const header of ['X-FG-Device-Id','X-FG-Device-Session','X-FG-Request-Id','X-FG-Timestamp','X-FG-Proof'])assert(membership.parameters.some(p=>p.name===header&&p.required));
 assert.equal(membership.responses['200'].content['application/json'].schema.$ref,ref('MembershipSnapshot'));
 const renewal=api.paths['/v1/bootstrap/sessions/renew'].post;
 assert(renewal.parameters.some(p=>p.name==='X-FG-Proof'&&p.required));
 assert.equal(renewal.requestBody.content['application/json'].schema.$ref,ref('RenewDeviceSessionRequest'));
 assert.equal(renewal.responses['200'].content['application/json'].schema.$ref,ref('RenewDeviceSessionResult'));
});
test('Keycloak template exposes only the public Android PKCE callback and API audience',()=>{
 const realm=JSON.parse(readFileSync(new URL('../../cloud/identity/realm-template.json',import.meta.url),'utf8'));
 const client=realm.clients.find(item=>item.clientId==='focusguard-android');
 assert(client);
 assert.equal(client.publicClient,true);
 assert.equal(client.standardFlowEnabled,true);
 assert.equal(client.implicitFlowEnabled,false);
 assert.equal(client.directAccessGrantsEnabled,false);
 assert.equal(client.serviceAccountsEnabled,false);
 assert.deepEqual(client.redirectUris,['com.totemmassmedia.focusguard:/oauth2redirect']);
 assert.equal(client.attributes['pkce.code.challenge.method'],'S256');
 assert(client.protocolMappers.some(mapper=>mapper.name==='focusguard-api-audience'
   && mapper.protocolMapper==='oidc-audience-mapper'
   && mapper.config['included.custom.audience']==='focusguard-api'
   && mapper.config['access.token.claim']==='true'
   && mapper.config['id.token.claim']==='false'));
});
test('Deletion never requires inventing a session to authorize account action',()=>assert(get('OperationAuthorizationRequest')({resourceId:id,intentDigest:digest,action:'AccountDeletion'})));
test('JCS known ordering, unicode and numeric encoding',()=>{
 assert.equal(canonicalize({z:1,a:'café',n:1e30}), '{"a":"café","n":1e+30,"z":1}');
 assert.equal(canonicalize({b:2,a:1}),canonicalize({a:1,b:2}));
 assert.throws(()=>canonicalize({a:Infinity}));
});
test('Schema validation alone does not claim cross-field authorization, duration or JCS duplicate detection',()=>{
 const expired={...intent,endAtUtc:'2026-10-10T20:01:00.000Z'};
 assert.equal(get('Intent')(expired),true,'Cross-field timing must be rejected by a future semantic validator');
});

