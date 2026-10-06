// Render the existing, attributed SVG mark into DPI-independent installer artwork.
// Usage: NODE_PATH=<playwright modules> node installer/render-art.cjs
const fs=require('node:fs');
const path=require('node:path');
const {chromium}=require('playwright');
const root=path.resolve(__dirname,'..');
const output=path.join(__dirname,'assets');
fs.mkdirSync(output,{recursive:true});
const logo=fs.readFileSync(path.join(root,'src/EndfieldIsland/Assets/EndfieldIcons/endfield-industries.svg'),'utf8')
 .replace(/<\?xml[^>]*\?>/g,'').replace(/<!--.*?-->/gs,'').replace(/<svg[\s\S]*?>/,'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 512 512">');
const mark=(x,y,size)=>`<g transform="translate(${x} ${y}) scale(${size/512})" fill="#d9e1df">${logo.replace(/<\/?svg[^>]*>/g,'')}</g>`;
const defs='<defs><pattern id="lines" width="12" height="12" patternUnits="userSpaceOnUse"><path d="M0 12L12 0" stroke="#779097" stroke-width=".6" opacity=".12"/></pattern></defs>';
const sidebar=`<svg xmlns="http://www.w3.org/2000/svg" width="656" height="1256" viewBox="0 0 328 628">${defs}
<rect width="328" height="628" fill="#172023"/><rect width="328" height="628" fill="url(#lines)"/>
<path d="M0 0H328V124H0Z" fill="#203438"/><path d="M0 0H7V628H0Z" fill="#13c8eb"/>
<g font-family="Segoe UI,sans-serif"><text x="28" y="39" font-size="12" letter-spacing="3" fill="#8de1ed">ENDFIELD</text>
<text x="28" y="66" font-size="11" letter-spacing="2" fill="#c6d6d6">DESKTOP SYSTEM</text>
<path d="M28 86H98" stroke="#e6e744" stroke-width="3"/><text x="273" y="94" font-size="24" fill="#13c8eb">↗</text>
${mark(28,155,272)}
<path d="M28 437H300" stroke="#526165"/><text x="28" y="466" font-size="11" fill="#8de1ed" letter-spacing="1.5">DYNAMIC ISLAND</text>
<text x="28" y="493" font-size="10" fill="#a2b2b2" letter-spacing="1">ASSISTANT · MUSIC · SYSTEM</text>
<path d="M28 533H51M58 533H157" stroke="#e6e744" stroke-width="2"/>
<text x="28" y="569" font-size="10" fill="#97aaad" letter-spacing="1">BUILT FOR YOUR DESKTOP.</text>
<text x="28" y="594" font-size="9" fill="#8de1ed" letter-spacing="2">WINDOWS / X64</text></g>
<path d="M296 562v42h-32" stroke="#13c8eb" fill="none" stroke-width="2"/></svg>`;
const header=`<svg xmlns="http://www.w3.org/2000/svg" width="360" height="120" viewBox="0 0 180 60">
<rect width="180" height="60" fill="#13c8eb"/><path d="M12 0H22V10H12ZM0 25H10V35H0ZM12 50H22V60H12Z" fill="#253e45"/>
<path d="M50 16h12v4h-6l10 10-3 3-10-10v6h-4V16Z" fill="#237baf"/>
<g fill="#1d464d"><circle cx="95" cy="23" r="10"/><path d="M80 43q0-13 15-13t15 13Z"/></g>
<g fill="none" stroke="#1d464d" stroke-width="2"><circle cx="122" cy="23" r="10"/><path d="M107 43q0-13 15-13t15 13Z"/></g>
<path d="M143 0v60M147 0v60M152 0v60" stroke="#a9d8df" stroke-width="2"/>
<rect x="157" width="23" height="60" fill="#bfc9c7"/><path d="M164 14h9m-9 3h9m-9 3h9m-9 3h9m-9 3h9m-9 3h9m-9 3h9m-9 3h9M168 6v5m-2-3h5" stroke="#6b7976"/>
<path d="M32 58H58M61 58H114M117 58H150" stroke-width="2" stroke="#e6e744"/></svg>`;
const background=`<svg xmlns="http://www.w3.org/2000/svg" width="1491" height="1080" viewBox="0 0 497 360">
<rect width="497" height="360" fill="#191d1d"/>
<path d="M333 0L497 164V0ZM497 270L407 360H497Z" fill="#1e282a"/>
<path d="M497 88L309 360M497 111L324 360" stroke="#2c393c" stroke-width=".7"/>
<path d="M8 32V8H38M489 298v54h-32" stroke="#3b696f" fill="none"/>
<path d="M20 328H54" stroke="#e6e744" stroke-width="2"/><path d="M60 328H166" stroke="#13c8eb" opacity=".6"/>
</svg>`;
(async()=>{
 const browser=await chromium.launch({headless:true,channel:'msedge'});
 try{for(const [name,svg,w,h] of [['sidebar',sidebar,656,1256],['header',header,360,120],['background',background,1491,1080]]){
  fs.writeFileSync(path.join(output,name+'.svg'),svg);
  const page=await browser.newPage({viewport:{width:w,height:h},deviceScaleFactor:1});
  await page.setContent(`<style>html,body{margin:0;overflow:hidden}svg{display:block}</style>${svg}`);
  await page.screenshot({path:path.join(output,name+'.png')});await page.close();
 }}finally{await browser.close();}
 console.log('Installer vector artwork rendered.');
})().catch(e=>{console.error(e);process.exitCode=1;});
