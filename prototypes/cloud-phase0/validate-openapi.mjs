import SwaggerParser from '@apidevtools/swagger-parser';
import {readContract} from './contracts.mjs';
const api=readContract();
await SwaggerParser.validate(api,{resolve:{external:false}});
console.log(JSON.stringify({validator:'swagger-parser 13.1.0',openapi:api.openapi,operations:Object.values(api.paths).reduce((n,p)=>n+Object.keys(p).length,0),result:'passed'}));

