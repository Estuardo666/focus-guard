import { readFileSync } from 'node:fs';
import Ajv2020 from 'ajv/dist/2020.js';
import { fileURLToPath } from 'node:url';
export const sourcePath = fileURLToPath(new URL('../../docs/cloud-phase0/CONTRACTS.md', import.meta.url));
export function readContract() {
 const text=readFileSync(sourcePath,'utf8');
 const block=text.match(/```json\r?\n([\s\S]*?)\r?\n```/);
 if(!block)throw new Error('Missing embedded OpenAPI');
 return JSON.parse(block[1]);
}
export function validators(){
 const api=readContract();
 const ajv=new Ajv2020({strict:false,validateFormats:false,allErrors:true});
 const root={$id:'urn:focusguard:phase0',components:{schemas:api.components.schemas}};
 ajv.addSchema(root);
 return {api, get(name){return ajv.getSchema('urn:focusguard:phase0#/components/schemas/'+name);}};
}

