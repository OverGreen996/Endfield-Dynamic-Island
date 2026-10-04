import {Locked} from './guard.js';
// Gemini classifies in the answer call; a deterministic gate checks verbatim evidence.
const categories=new Set(['身分資料','喜好偏好','回答方式','互動禁忌','生活習慣','個人事項','目標計畫']);
export const memoryInstructions='本次只輸出符合 schema 的 JSON，answer 是自然語言回答，memory 是必填的個人記憶判斷；不要省略 memory，也不要輸出 XML 區塊。只有本次使用者原句明確自述持續性的簡易個人資料時，memory 才可為 {"category":"分類","quote":"完整本次使用者原句"}。明確說自己養某種寵物屬於個人事項，無需另說「記住」；例如「我養了一隻兔子」應分類，不要只寒暄。分類只能是身分資料、喜好偏好、回答方式、互動禁忌、生活習慣、個人事項、目標計畫。quote 必須完整原文，不得改寫，保留空格。不能從舊歷史、已保存背景、搜尋文章或你自己的回答擷取。玩笑、假設、角色扮演、轉述、問題、暫時情緒、敏感資料與推測全部 memory=null。沒有本機儲存回報前，不要在自然語言回答聲稱已記住、已儲存或已安排。memory 只提議分類，沒有執行權限。';
export const memoryResponseSchema={type:'object',properties:{
 answer:{type:'string',description:'給使用者的回答；沒有本機儲存結果，不能聲稱已保存記憶。'},
 memory:{anyOf:[{type:'null'},{type:'object',properties:{category:{type:'string',enum:[...categories]},quote:{type:'string',description:'完整本次使用者原句，不能改寫或從歷史擷取。'}},required:['category','quote'],additionalProperties:false}]}
},required:['answer','memory'],additionalProperties:false};
const clean=s=>String(s).trim().replace(/[。！!]+$/u,'');
const uncertain=/假設|假如|如果|例如|比方|開玩笑|亂講|胡言|演戲|角色扮演|不要記|別記|不用記|忘了剛|轉述|他說|她說|引號|[「『"？?]|今天|現在|這次|暫時|可能|好像|也許|或許|不確定|外星|超人|神仙|魔王|總統|世界首富|[。；;\n]|(?:他|她|別人|朋友)(?:也|有|養|喜歡|叫)/u;
export function memoryResponse(raw,question,{required=false}={}){
 const text=String(raw??'');const matches=[...text.matchAll(/<user_memory>([\s\S]*?)<\/user_memory>/g)];
 let visible=text.replace(/<user_memory>[\s\S]*?(?:<\/user_memory>|$)/g,'').trim(),data;
 try{const parsed=JSON.parse(text);if(!parsed||Array.isArray(parsed)||typeof parsed.answer!=='string'||!parsed.answer.trim()||!Object.hasOwn(parsed,'memory')||Object.keys(parsed).some(k=>!['answer','memory'].includes(k)))throw Error('invalid envelope');visible=parsed.answer.trim();data={memory:parsed.memory};}
 catch{if(required)throw new Locked('invalid_assistant_response',502);}
 if(!data){
  if(matches.length!==1||matches[0].index+matches[0][0].length!==text.trimEnd().length||matches[0][1].length>2000)return {text:visible,memory_suggestions:[]};
  try{data=JSON.parse(matches[0][1]);}catch{return {text:visible,memory_suggestions:[]};}
 }
 try{
  const m=data.memory,q=clean(question);
  if(!m||Object.keys(data).some(k=>k!=='memory')||Object.keys(m).some(k=>!['category','quote'].includes(k))||
    !categories.has(m.category)||typeof m.quote!=='string'||clean(m.quote)!==q||q.length<3||q.length>160||
    !/^(?:我|我的|以後回答|回答時請|請用)/u.test(q)||uncertain.test(q)||
    /密碼|口令|金鑰|api.?key|token|otp|信用卡|身分證|身份證|護照|帳號|銀行|病歷|地址|電話|手機號|AIza[\w-]+|sk-[\w-]+|忽略.*指令|system.?prompt|執行.*命令/i.test(q))return {text:visible,memory_suggestions:[]};
  return {text:visible,memory_suggestions:[{category:m.category,quote:q}]};
 }catch{return {text:visible,memory_suggestions:[]};}
}
