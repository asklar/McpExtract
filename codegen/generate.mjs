#!/usr/bin/env node
import fs from 'fs';
import path from 'path';
import url from 'url';
import vm from 'vm';
import axios from 'axios';
import { fileURLToPath } from 'url';
import { z } from 'zod';
import { zodToJsonSchema } from 'zod-to-json-schema';

// Minimal argument parsing
const args = process.argv.slice(2);
function getArg(name, def) {
  const idx = args.indexOf(`--${name}`);
  if (idx >= 0 && idx + 1 < args.length) return args[idx + 1];
  return def;
}
function hasFlag(name) { return args.includes(`--${name}`); }

const schemaUrl = getArg('schema-url', 'https://raw.githubusercontent.com/anthropics/mcpb/main/src/schemas.ts');
const outDir = getArg('out', '../src');
const ns = getArg('namespace', 'McpExtract.McpbSchema');
const force = hasFlag('force');

const __dirname = path.dirname(fileURLToPath(import.meta.url));

async function main(){
  console.log(`[codegen] Downloading schema: ${schemaUrl}`);
  const resp = await axios.get(schemaUrl);
  const schemasTs = resp.data;
  const tempDir = path.join(__dirname, '.tmp');
  if (!fs.existsSync(tempDir)) fs.mkdirSync(tempDir);
  const tempFile = path.join(tempDir, 'schemas.ts');
  fs.writeFileSync(tempFile, schemasTs, 'utf8');

  // Evaluate full schema file to preserve declaration order and dependencies.
  // Transform exports so variables become global (var) and remove imports/types.
  const exportConstRegex = /export const (\w+)/g;
  const exportedNames = new Set();
  let m; while((m = exportConstRegex.exec(schemasTs)) !== null){ exportedNames.add(m[1]); }

  let transformed = schemasTs
    .replace(/^[ \t]*import[^;]+;.*$/mg, '')
    .replace(/export const /g, 'var ')
    .replace(/export interface [^{]+{[^}]+}/g, '')
    .replace(/export type [^=]+=[^;]+;/g, '')
    .replace(/\/\*.*?\*\//gs, match=>match); // keep block comments

  // Some TS specific constructs may still exist; basic removal of ': <type>' in const declarations where safe
  transformed = transformed.replace(/(var \w+)\s*:\s*[^=;]+=/g, '$1 =');

  const sandbox = { z, console };
  sandbox.global = sandbox;
  const context = vm.createContext(sandbox);
  try {
    vm.runInContext(transformed, context, { timeout: 3000 });
  } catch (err) {
    console.warn('[codegen] Full-file evaluation error; falling back to per-export eval:', err);
  }

  // Collect exported schemas present and Zod instances only
  const zodSchemas = {};
  for (const name of Array.from(exportedNames)) {
    const val = context[name];
    if (val && typeof val === 'object' && typeof val._def === 'object') {
      zodSchemas[name] = val;
    }
  }
  const names = Object.keys(zodSchemas).sort();
  console.log(`[codegen] Collected ${names.length} Zod schemas after full-file evaluation`);

  // Convert each to JSON Schema collecting all definitions
  const rawDefinitions = {}; // originalName -> schema
  for (const n of names) {
    try {
      const js = zodToJsonSchema(zodSchemas[n], { name: n, basePath: [n] });
      if (js.definitions) {
        for (const [k, v] of Object.entries(js.definitions)) {
          rawDefinitions[k] = v;
        }
      } else {
        rawDefinitions[n] = js;
      }
    } catch (err) {
      console.warn(`[codegen] Failed zodToJsonSchema for ${n}: ${err}`);
    }
  }

  // Ensure top-level schemas also captured if no definitions
  for (const n of names) {
    if (!rawDefinitions[n]) {
      try {
        const js = zodToJsonSchema(zodSchemas[n], { name: n, basePath: [n] });
        rawDefinitions[n] = js;
      } catch {/* ignore */}
    }
  }

  // Simple JSON Schema -> C# type mapping
  function pascalCase(str){
    if(!str) return 'Unnamed';
    return str
      .replace(/([a-z0-9])([A-Z])/g,'$1 $2')
      .replace(/[^A-Za-z0-9]+/g,' ')
      .trim()
      .split(/\s+/)
      .map(s=> s.charAt(0).toUpperCase() + s.slice(1))
      .join('');
  }
  function simplifyName(name){
    let n = name;
    const prefixes = ['McpbManifest', 'Mcpb', 'Manifest', 'Schema', 'Zod'];
    for (const p of prefixes) {
      if (n.startsWith(p) && n.length > p.length) {
        n = n.substring(p.length);
        break;
      }
    }
    const suffixes = ['Schema','Type','Model','Definition'];
    for (const s of suffixes) {
      if (n.endsWith(s) && n.length > s.length) {
        n = n.substring(0, n.length - s.length);
        break;
      }
    }
    if(!n) n = name;
    if(/^Enum$/i.test(n)) n = 'Platform'; // heuristic: lone Enum becomes Platform (observed platform enum)
    if(/^Object$/i.test(n)) n = 'Data';
    return pascalCase(n);
  }

  // Assign C# names ensuring uniqueness
  const nameMap = {}; // original -> csName
  const usedNames = new Set();
  const schemaSignature = sch => {
    if(!sch || typeof sch !== 'object') return 'null';
    if(sch.$ref) return `ref:${sch.$ref}`;
    if(sch.enum) return 'enum:' + sch.enum.map(v=>typeof v+':'+v).join('|');
    const t = sch.type;
    if(t === 'object' && sch.properties){
      const keys = Object.keys(sch.properties).sort();
      return 'obj{' + keys.map(k=> k+':'+schemaSignature(sch.properties[k]) ).join(',') + '}';
    }
    if(t === 'array') return 'arr<' + schemaSignature(sch.items) + '>';
    if(Array.isArray(t)) return 'multi<' + t.sort().join('&') + '>';
    return String(t||'any');
  };
  const signatureToName = new Map();
  function shortHash(str){
    let h = 0, i, chr;
    for(i=0;i<str.length;i++){ chr=str.charCodeAt(i); h = ((h<<5)-h)+chr; h|=0; }
    // convert to base36 positive
    const num = (h>>>0).toString(36);
    return num.slice(0,5);
  }
  function detectPlatformEnum(sch){
    if(!sch || !Array.isArray(sch.enum)) return false;
    const vals = sch.enum;
    const platformVals = ['darwin','win32','linux'];
    return vals.length === platformVals.length && platformVals.every(v=> vals.includes(v));
  }
  const originalNames = Object.keys(rawDefinitions).sort();
  for (const oName of originalNames) {
    const sch = rawDefinitions[oName];
    const sig = schemaSignature(sch);
    let candidate = simplifyName(oName);
    if(isStringEnum(sch) && detectPlatformEnum(sch)){
      candidate = 'Platform';
    }
    if(sch && sch.enum){
      // For enums: do NOT reuse name even if structural signature matches another enum.
      // Ensure uniqueness by hashing original name on collision.
      if(usedNames.has(candidate)){
        const hash = shortHash(oName);
        candidate = candidate + hash.toUpperCase();
      }
      nameMap[oName] = candidate;
      usedNames.add(candidate);
      // Do NOT record in signatureToName to avoid collapsing other enums.
      continue;
    }
    if(signatureToName.has(sig)){
      nameMap[oName] = signatureToName.get(sig);
      continue;
    }
    if(usedNames.has(candidate)){
      // Different signature: append short structural hash for uniqueness
      const hash = shortHash(sig);
      const base = candidate;
      candidate = base + hash.charAt(0).toUpperCase() + hash.slice(1);
      // If still collides (very unlikely), append another hash slice iteratively
      let iter = 1;
      while(usedNames.has(candidate)){
        const extra = shortHash(sig + ':' + iter);
        candidate = base + extra.toUpperCase();
        iter++;
      }
    }
    nameMap[oName] = candidate;
    usedNames.add(candidate);
    signatureToName.set(sig, candidate);
  }

  function isStringEnum(schema){
    return Array.isArray(schema.enum) && schema.enum.every(v => typeof v === 'string');
  }

  function isNumericEnum(schema){
    return Array.isArray(schema.enum) && schema.enum.every(v => typeof v === 'number');
  }

  function mapRef(ref, propContext){
    const seg = ref.split('/').pop();
    const target = rawDefinitions[seg];
    if(!target){
      console.warn(`[codegen] Warning: referenced definition '${seg}' not found.`);
      return pascalCase(seg);
    }
    if(!target.enum){
      return nameMap[seg] || pascalCase(seg);
    }
    // Enum handling
    if(detectPlatformEnum(target)){
      nameMap[seg] = 'Platform';
      usedNames.add('Platform');
      return 'Platform';
    }
    const signature = 'enum:' + target.enum.map(v=>typeof v+':'+v).join('|');
    if(currentClassName && propContext){
      let base = currentClassName + pascalCase(propContext);
      // If a name already taken, check if identical signature; if identical reuse, else suffix
      if(usedNames.has(base)){
        const existingOrigin = Object.entries(nameMap).find(([,v])=>v===base)?.[0];
        if(existingOrigin){
          const existingSchema = rawDefinitions[existingOrigin];
          if(existingSchema && existingSchema.enum){
            const existingSig = 'enum:' + existingSchema.enum.map(v=>typeof v+':'+v).join('|');
            if(existingSig === signature){
              return base; // identical reuse
            }
          }
        }
        // Create unique variant name
        let suffix = 2; let variant = base + suffix;
        while(usedNames.has(variant)) { suffix++; variant = base + suffix; }
        base = variant;
      }
      // Create clone under context-specific origin
      const origin = seg + '_' + base;
      if(!rawDefinitions[origin]){
        const clone = JSON.parse(JSON.stringify(target));
        rawDefinitions[origin] = clone;
      }
      nameMap[origin] = base;
      usedNames.add(base);
      return base;
    }
    // Fallback: use existing mapping or assign one
    if(!nameMap[seg]){
      let base = pascalCase(seg);
      if(usedNames.has(base)){
        let i=2; let variant = base + i; while(usedNames.has(variant)){ i++; variant = base + i; }
        base = variant;
      }
      nameMap[seg] = base;
      usedNames.add(base);
    }
    return nameMap[seg];
  }

  function createSyntheticName(base){
    // base may include 'InlineObject' etc.; strip generic words and 'Object'
    let cleaned = base.replace(/Object$/,'').replace(/Inline$/,'');
    if(!cleaned) cleaned = base;
    let candidate = pascalCase(cleaned);
    let i = 2;
    while(usedNames.has(candidate)) { candidate = pascalCase(cleaned) + i; i++; }
    usedNames.add(candidate);
    return candidate;
  }

  function singularize(word){
    if(!word) return word;
    const lower = word.toLowerCase();
    if(lower === 'tools') return 'Tool';
    if(lower === 'prompts') return 'Prompt';
    if(lower.endsWith('ies')) return pascalCase(lower.slice(0,-3) + 'y');
    if(lower.endsWith('ses')) return pascalCase(lower.slice(0,-2));
    if(lower.endsWith('s')) return pascalCase(lower.slice(0,-1));
    return pascalCase(word);
  }

  function mapType(schema, propContext){
    if(!schema) return 'object';
  if(schema.$ref) return mapRef(schema.$ref, propContext);
    // Keep track of origin for naming later
    if(schema.anyOf || schema.oneOf){
      const variants = schema.anyOf || schema.oneOf;
      // optional (null | T)
      const nonNull = variants.filter(v => v.type !== 'null');
      if(variants.length === 2 && nonNull.length === 1){
        const inner = mapType(nonNull[0], propContext);
        if(['int','double','bool','DateTime'].includes(inner) || /^[A-Z]/.test(inner)){
          return inner + '?';
        }
        return inner; // reference types already nullable
      }
      return 'object';
    }
    if(schema.enum){
      // Assign a name for inline enums if they do not already have one
      if(!nameMap[schema.__origin]){
        // Derive candidate from property context
        // Contextual enum naming for better domain semantics
        let base = (currentClassName ? currentClassName : '') + (propContext ? pascalCase(propContext) : 'Enum');
        if(detectPlatformEnum(schema)) base = 'Platform';
        // Platform enum normalization (reuse existing Platform enum if identical)
        if(base !== 'Platform' && usedNames.has(base)){
          // differentiate identical names across properties of same class
          const sig = schema.enum.map(v=>v).join('|');
          base = base + shortHash(sig).toUpperCase();
        }
        let candidate = base;
        let i=2;
        while(usedNames.has(candidate)) { candidate = base + i; i++; }
        // Give enum its own origin if inline to avoid collision with same signature in another class
        if(!schema.__origin){
          const originBase = (currentClassName ? currentClassName + '_' : '') + (propContext?propContext:'Enum');
          let origin = originBase;
          let counter = 2;
          while(rawDefinitions[origin]){ origin = originBase + '_' + counter; counter++; }
          Object.defineProperty(schema, '__origin', { value: origin, enumerable: false });
        }
        nameMap[schema.__origin] = candidate;
        usedNames.add(candidate);
      }
      return nameMap[schema.__origin];
    }
    const t = schema.type;
    if(Array.isArray(t)){
      if(t.includes('null')){
        const nonNull = t.filter(x=>x!=='null');
        if(nonNull.length===1){
          const inner = mapType({ ...schema, type: nonNull[0] }, propContext);
            if(['int','double','bool','DateTime'].includes(inner) || /^[A-Z]/.test(inner)){
              return inner + '?';
            }
            return inner;
        }
      }
      return 'object';
    }
    switch(t){
      case 'string': return 'string';
      case 'integer': return 'int';
      case 'number': return 'double';
      case 'boolean': return 'bool';
      case 'array': {
        // Special-case tools/prompts arrays
        if(propContext){
          const singular = singularize(propContext);
          if(singular === 'Tool' || singular === 'Prompt'){
            // Ensure corresponding class scaffold exists if item is object
            if(schema.items && (schema.items.type === 'object' || schema.items.$ref)){
              const targetName = singular;
              if(!Object.values(nameMap).includes(targetName)){
                // create placeholder definition if not present
                rawDefinitions[targetName] = rawDefinitions[targetName] || { type:'object', properties:{}, required:[] };
                nameMap[targetName] = targetName;
              }
              return `List<${targetName}>`;
            }
          }
        }
        return `List<${mapType(schema.items, propContext ? singularize(propContext) : propContext)}>`;
      }
      case 'object': {
        // Dictionary-like object with additionalProperties
        if(schema.additionalProperties && typeof schema.additionalProperties === 'object'){
          const valueSchema = schema.additionalProperties;
          // Try reuse existing known object type (e.g., McpConfig) by structural signature
          const valSig = schemaSignature(valueSchema);
          for(const [origDef, defSchema] of Object.entries(rawDefinitions)){
            if(defSchema && defSchema.type==='object'){
              const sig2 = schemaSignature(defSchema);
              if(sig2 === valSig){
                const reuseName = nameMap[origDef];
                if(reuseName){
                  return `Dictionary<string, ${reuseName}>`;
                }
              }
            }
          }
          const valueType = mapType(valueSchema, propContext ? propContext + 'Value' : 'Value');
          return `Dictionary<string, ${valueType}>`;
        }
        const mapped = nameMap[schema.__origin];
        if(mapped) return mapped;
        // Inline anonymous object: generate synthetic name from prop context
        const synthetic = createSyntheticName(propContext || 'Inline');
        nameMap[schema.__origin || synthetic] = synthetic;
        if(!schema.__origin){ Object.defineProperty(schema, '__origin', { value: synthetic, enumerable: false }); }
        rawDefinitions[schema.__origin || synthetic] = schema;
        return synthetic;
      }
      default: return 'object';
    }
  }

  const classDefs = [];
  const enumDefs = [];
  const stringEnumDefs = [];
  const seenClasses = new Set();
  const seenEnums = new Set();
  const seenStringEnums = new Set();
  let currentClassName = '';

  function canPromoteStringEnum(schema){
    if(!isStringEnum(schema)) return false;
    return schema.enum.every(v => /^[a-z0-9_\-]+$/i.test(v));
  }

  function safeEnumMemberName(value){
    let name = value.replace(/[^A-Za-z0-9]+/g,' ')
      .trim()
      .split(/\s+/)
      .map(s=> s.charAt(0).toUpperCase() + s.slice(1))
      .join('');
    if(!name) name = 'Value';
    if(/^[0-9]/.test(name)) name = 'V' + name; // prefix digit-starting names
    if(name === 'Enum') name = name + 'Value';
    return name;
  }

  function emitEnum(name, schema){
    if(isStringEnum(schema) && canPromoteStringEnum(schema)){
      if(seenEnums.has(name)) return;
      seenEnums.add(name);
      const members = schema.enum.map(v => {
        const memberName = safeEnumMemberName(v);
        return `    [JsonStringEnumMemberName("${v}")] ${memberName}`;
      });
      enumDefs.push(`public enum ${name} {\n${members.join(',\n')}\n}`);
      return;
    }
    if(isStringEnum(schema)) return emitStringEnum(name, schema);
    if(seenEnums.has(name)) return;
    seenEnums.add(name);
    const members = schema.enum.map(v=> 'V' + String(v).replace(/[^A-Za-z0-9]+/g,'_')).filter((v,i,a)=>a.indexOf(v)===i);
    enumDefs.push(`public enum ${name} { ${members.join(', ')} }`);
  }
  function emitStringEnum(name, schema){
    if(seenStringEnums.has(name)) return;
    seenStringEnums.add(name);
    const members = schema.enum.map(v=> ({constName: pascalCase(String(v)), value: v}));
    const lines = members.map(m=> `    public const string ${m.constName} = "${m.value}";`);
    stringEnumDefs.push(`public static class ${name} {\n${lines.join('\n')}\n}`);
  }

  function markOrigin(schema, origin){
    if(schema && typeof schema === 'object' && !schema.__origin){
      Object.defineProperty(schema, '__origin', { value: origin, enumerable: false });
    }
  }
  for (const [orig, sch] of Object.entries(rawDefinitions)) {
    markOrigin(sch, orig);
  }

  function emitClass(originalName, schema){
    const name = nameMap[originalName] || pascalCase(originalName);
    if(seenClasses.has(name)) return;
    if(schema.enum){ emitEnum(name, schema); return; }
    if(schema.type !== 'object' || !schema.properties){ return; }
    seenClasses.add(name);
    const prevClass = currentClassName;
    currentClassName = name;
    const props = schema.properties;
    const required = new Set(schema.required || []);
    const propLines = [];
    for(const [p, sch] of Object.entries(props)){
      markOrigin(sch, name + '_' + p + '_Type');
      let csType = mapType(sch, p);
      // Generic convention: if property named 'type' references an enum defined for a different class, clone it so each class gets its own enum.
      if(p === 'type' && sch.$ref){
        const seg = sch.$ref.split('/').pop();
        const target = rawDefinitions[seg];
        if(target && target.enum && !detectPlatformEnum(target)){
          const desired = name + 'Type';
          if(csType !== desired){
            if(!usedNames.has(desired)){
              // Clone schema for distinct enum
              const clone = JSON.parse(JSON.stringify(target));
              const cloneOrigin = seg + '_' + desired;
              if(!rawDefinitions[cloneOrigin]) rawDefinitions[cloneOrigin] = clone;
              nameMap[cloneOrigin] = desired;
              usedNames.add(desired);
              csType = desired;
            } else {
              csType = desired; // reuse existing desired enum if already created
            }
          }
        }
      }
      const isValue = ['int','double','bool','DateTime'].includes(csType.replace('?',''));
      const isCollection = csType.startsWith('List<') || csType.startsWith('Dictionary<');
      const optional = (!required.has(p) || (sch.anyOf||sch.oneOf||Array.isArray(sch.type) && sch.type.includes('null')));
      let finalType = csType;
      // Special-case user_config to be Dictionary<string, UserConfigurationOption>
      if(p === 'user_config'){
        finalType = 'Dictionary<string, UserConfigurationOption>';
      }
      // Special-case platform_overrides to dictionary of McpConfig (per-platform overrides)
      if(p === 'platform_overrides'){
        finalType = 'Dictionary<string, McpConfig>';
      }
      if(optional){
        if(isValue && !finalType.endsWith('?')) finalType += '?';
        else if(!isValue && !finalType.endsWith('?') && !isCollection){
          finalType += '?';
        } else if(isCollection && !finalType.endsWith('?')){
          finalType += '?';
        }
      }
      const attr = p === pascalCase(p) ? '' : `    [JsonPropertyName("${p}")]\n`;
      propLines.push(`${attr}    public ${finalType} ${pascalCase(p)} { get; set; }`);
      // handle nested inline object/enum arrays
      if(sch.$ref){ /* referenced definition already processed in second pass */ }
      else if(sch.type === 'object'){
  const inlineName = mapType(sch, p);
        if(!rawDefinitions[sch.__origin]){
          rawDefinitions[sch.__origin] = sch; // add for emission
          nameMap[sch.__origin] = inlineName;
        }
      } else if(sch.enum){
        const enumName = mapType(sch, p);
        if(!rawDefinitions[sch.__origin]){
          rawDefinitions[sch.__origin] = sch;
        }
      } else if(sch.type === 'array' && sch.items){
        const it = sch.items;
        if(it.$ref){/* skip */}
        else if(it.type === 'object'){
          const inlineName = mapType(it, p);
          if(!rawDefinitions[it.__origin]) {
            rawDefinitions[it.__origin] = it;
            nameMap[it.__origin] = inlineName;
          }
        } else if(it.enum){
          const inlineName = mapType(it, p);
          if(!rawDefinitions[it.__origin]) {
            rawDefinitions[it.__origin] = it;
            nameMap[it.__origin] = inlineName;
          }
        }
      }
    }
    classDefs.push(`public class ${name} {\n${propLines.join('\n')}\n}`);
    currentClassName = prevClass;
  }

  // First emit all root definitions in stable order
  for(const orig of originalNames){
    const sch = rawDefinitions[orig];
    if(!sch) continue;
    emitClass(orig, sch);
  }

  // Second pass: emit any definitions added during inline discovery
  for(const orig of Object.keys(rawDefinitions)){
    const sch = rawDefinitions[orig];
    emitClass(orig, sch);
  }

  // ----- Deduplication Phase (objects only) -----
  const allNamesPost = Object.keys(rawDefinitions).sort();
  const sigToCanonical = new Map();
  for(const orig of allNamesPost){
    const sch = rawDefinitions[orig];
    if(sch && sch.enum) continue; // skip enums
    const sig = schemaSignature(sch);
    if(sigToCanonical.has(sig)){
      // reuse canonical name
      const canon = sigToCanonical.get(sig);
      nameMap[orig] = nameMap[canon];
    } else {
      sigToCanonical.set(sig, orig);
    }
  }
  // Re-emit after dedup
  classDefs.length = 0;
  enumDefs.length = 0;
  stringEnumDefs.length = 0;
  seenClasses.clear();
  seenEnums.clear();
  seenStringEnums.clear();
  for(const orig of allNamesPost){
    emitClass(orig, rawDefinitions[orig]);
  }

  const hash = (await import('crypto')).createHash('sha256').update(schemasTs).digest('hex').slice(0,12);
  const header = `// <auto-generated />\n// Source: ${schemaUrl}\n// SHA256: ${hash}\n// Generation Time: ${new Date().toISOString()}\n// WARNING: Generated code. Edits may be overwritten.\n#nullable enable\nusing System.Collections.Generic;\nusing System.Text.Json;\nusing System.Text.Json.Serialization;\nnamespace ${ns};\n\npublic static class McpbSchemaInfo { public const string SourceHash = \"${hash}\"; public const string SourceUrl = \"${schemaUrl}\"; }\n`;

  const body = [
    ...enumDefs.sort(),
    ...stringEnumDefs.sort(),
    ...classDefs.sort()
  ].join('\n\n');
  // Post-process to remove any lingering 'Object' suffix artifacts in names if they slipped through
  let finalContent = header + '\n' + body + '\n';
  finalContent = finalContent.replace(/class (\w+Object) /g, (m,g)=> m.replace('Object ',' '));
  finalContent = finalContent.replace(/static class (\w+Object) /g, (m,g)=> m.replace('Object ',' '));
  // Rename generic Enum to Platform if it matches platform values and not already named
  finalContent = finalContent.replace(/public enum Enum \{[\s\S]*?JsonStringEnumMemberName\("darwin"\)[\s\S]*?JsonStringEnumMemberName\("win32"\)[\s\S]*?JsonStringEnumMemberName\("linux"\)/, match => match.replace('enum Enum','enum Platform'));
  // Replace Default property type to JsonElement?
  finalContent = finalContent.replace(/public object\? Default /, 'public JsonElement? Default ');

  if (!fs.existsSync(outDir)) fs.mkdirSync(outDir, { recursive: true });
  const outFile = path.join(outDir, 'McpbSchemaModels.cs');
  if(process.env.CODEGEN_DEBUG === '1'){
    console.log('[codegen] Final type name map:');
    for(const [orig, cs] of Object.entries(nameMap)){
      console.log(`  ${orig} -> ${cs}`);
    }
  }
  if (fs.existsSync(outFile) && !force) {
    const existing = fs.readFileSync(outFile, 'utf8');
    if (existing.includes(hash)) {
      console.log('[codegen] No changes detected (hash match). Skipping. Use --force to overwrite.');
      return;
    }
  }
  fs.writeFileSync(outFile, finalContent, 'utf8');
  console.log(`[codegen] Wrote ${outFile}`);
}

main().catch(e=>{console.error(e);process.exit(1);});
