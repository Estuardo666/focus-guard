import test from 'node:test';
import assert from 'node:assert/strict';
import {execFileSync,spawnSync} from 'node:child_process';
import {fileURLToPath} from 'node:url';
import canonicalize from 'canonicalize';
const project=fileURLToPath(new URL('./CryptoProbe/CryptoProbe.csproj',import.meta.url));
function csharp(text){return execFileSync('dotnet',['run','--project',project,'-c','Release','--no-build','--','--canonicalize'],{input:text,encoding:'utf8',timeout:60000}).trim();}
test('Reference C# and canonicalize agree on recursively sorted Unicode and numeric vectors',()=>{
 const vectors=[{z:1,a:'café',n:1e30},{numbers:[333333333.33333329,1e30,4.5,0.002,1e-27],literals:[null,true,false]}, {'😀':0,'€':1,'a':2}, {nested:{z:2,a:'x'},array:[{y:1,b:2},null]}, {control:'\u000f\n\t/"\\',negativeZero:-0}];
 for(const v of vectors)assert.equal(csharp(JSON.stringify(v)),canonicalize(v));
});
test('C# reference parser rejects duplicate properties and malformed JSON before signing',()=>{
 for(const text of ['{"a":1,"a":2}','{"a":','{"number":NaN}']){
  const r=spawnSync('dotnet',['run','--project',project,'-c','Release','--no-build','--','--canonicalize'],{input:text,encoding:'utf8',timeout:60000});
  assert.notEqual(r.status,0,text);
 }
});
test('C# transport rejects invalid UTF8 rather than signing replacement characters',()=>{
 const malformed=Buffer.concat([Buffer.from('{"a":"'),Buffer.from([0xff]),Buffer.from('"}')]);
 const r=spawnSync('dotnet',['run','--project',project,'-c','Release','--no-build','--','--canonicalize'],{input:malformed,encoding:'utf8',timeout:60000});
 assert.notEqual(r.status,0);
});
