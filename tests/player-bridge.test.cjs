const test=require('node:test'),assert=require('node:assert/strict'),vm=require('node:vm'),fs=require('node:fs'),path=require('node:path');
const source=fs.readFileSync(path.join(__dirname,'../src/MusicPlayerHost/PlayerBridge.js'),'utf8');
const ids=Array.from({length:6},(_,i)=>'v'+String(i).padStart(10,'0'));
function page({index=0,list=ids,ad=false}={}){
 let track=index,random=0,clockOffset=0,ended;const jumps=[],video={readyState:4,paused:false,currentTime:10,duration:120,play:()=>Promise.resolve()};
 const player={getPlaylist:()=>list,getPlaylistIndex:()=>track,getVideoData:()=>({video_id:list[track],title:'Track '+track,author:'Test'}),
  playVideoAt:i=>{track=i;jumps.push(i);},playVideo(){video.paused=false;},pauseVideo(){video.paused=true;},
  // Reproduce the reported provider behavior: native Next always selects item two.
  nextVideo(){track=1;jumps.push(1);},previousVideo(){track=Math.max(0,track-1);},getShuffle:()=>false,setShuffle(){},
  seekTo(value){video.currentTime=value;},setLoop(){},setLoopVideo(v){this.loopVideo=v;},getLoopVideo(){return !!this.loopVideo;}};
 const context={window:{},document:{getElementById:()=>player,querySelector:s=>s==='video'?video:s==='#movie_player.ad-showing'&&ad?{}:null,addEventListener:(name,fn)=>{if(name==='ended')ended=fn;}},
  location:{pathname:'/watch',href:'https://www.youtube.com/watch?v='+list[track],origin:'https://www.youtube.com'},navigator:{mediaSession:{}},
  Math:Object.assign(Object.create(Math),{random:()=>[.8,.2,.6,.4,.1,.9][random++%6]}),Date:{now:()=>Date.now()+clockOffset},URL,HTMLMediaElement:{prototype:{pause(){this.paused=true;}}}};
 vm.runInNewContext(source,context);const controller=context.window.islandMusic;
 return {controller,jumps,video,current:()=>list[track],index:()=>track,advanceTime(ms){clockOffset+=ms;},nativeAdvance(){track=(track+1)%list.length;video.currentTime=0;},ended(){let stopped=false;ended({target:{tagName:'VIDEO'},stopImmediatePropagation(){stopped=true;}});return stopped;}};
}
test('shuffle Next bypasses provider fixed-second behavior and exhausts the unplayed bag',()=>{
 const p=page();assert.equal(p.controller.command({kind:'Shuffle',value:1}),true);
 const played=[p.current()];for(let i=0;i<ids.length-1;i++){assert.equal(p.controller.command({kind:'Next'}),true);played.push(p.current());p.controller.snapshot();}
 assert.notEqual(played[1],ids[1]);assert.equal(new Set(played).size,ids.length);assert.equal(p.controller.snapshot().shuffle,true);
 assert.equal(p.controller.command({kind:'Next'}),true);assert.notEqual(p.current(),played.at(-1));
});
test('full page replacement keeps shuffle, remaining tracks and Previous/Next history',()=>{
 let p=page();p.controller.command({kind:'Shuffle',value:1});p.controller.command({kind:'Next'});p.controller.snapshot();
 const first=p.current(),queue=JSON.parse(JSON.stringify(p.controller.exportQueue()));
 p=page({index:ids.indexOf(first)});p.controller.restore({shuffle:true,repeat:0,queue});
 assert.equal(p.controller.snapshot().shuffle,true);p.controller.command({kind:'Next'});p.controller.snapshot();const second=p.current();
 assert.notEqual(second,first);assert.notEqual(second,ids[0]);
 p.controller.command({kind:'Previous'});p.controller.snapshot();assert.equal(p.current(),first);
 p.controller.command({kind:'Next'});p.controller.snapshot();assert.equal(p.current(),second);
});
test('automatic track end follows random queue and stops when repeat is off',()=>{
 const p=page();p.controller.command({kind:'Shuffle',value:1});const seen=new Set([p.current()]);
 for(let i=1;i<ids.length;i++){assert.equal(p.ended(),true);seen.add(p.current());p.controller.snapshot();}
 assert.equal(seen.size,ids.length);assert.equal(p.ended(),true);assert.equal(p.video.paused,true);
 p.controller.command({kind:'Repeat',value:1});assert.equal(p.ended(),true);assert.equal(p.video.paused,false);
});
test('disabled shuffle uses provider Next; one-item playlists do not pretend to shuffle',()=>{
 const p=page();p.controller.command({kind:'Shuffle',value:1});p.controller.command({kind:'Shuffle',value:0});p.controller.command({kind:'Next'});
 assert.equal(p.current(),ids[1]);assert.equal(p.controller.exportQueue(),null);
 const single=page({list:ids.slice(0,1)});assert.equal(single.controller.command({kind:'Shuffle',value:1}),false);assert.equal(single.controller.snapshot().canShuffle,false);
});
test('temporary missing playlist metadata retains remaining songs and history',()=>{
 const mutable=[...ids],p=page({list:mutable});p.controller.command({kind:'Shuffle',value:1});p.controller.command({kind:'Next'});p.controller.snapshot();
 const before=JSON.stringify(p.controller.exportQueue());const original=[...mutable];mutable.length=0;
 assert.equal(JSON.stringify(p.controller.exportQueue()),before);mutable.push(...original);
 assert.equal(p.controller.command({kind:'Next'}),true);assert.equal(p.controller.snapshot().shuffle,true);
});
test('invalid restoration state cannot become a playable queue',()=>{
 const p=page();p.controller.restore({shuffle:true,queue:{bag:['not-a-video'],history:[ids[0]],cursor:0,pending:'',pendingUntil:0}});
 assert.equal(p.controller.exportQueue().bag.includes('not-a-video'),false);
});

test('single-track repeat intercepts track end even when YouTube clears the native loop flag',()=>{
 for(const shuffle of [false,true]){
  const p=page();if(shuffle)p.controller.command({kind:'Shuffle',value:1});
  p.controller.command({kind:'Repeat',value:2});const id=p.current(),queue=JSON.stringify(p.controller.exportQueue());
  p.video.loop=false;p.video.paused=true;p.video.currentTime=p.video.duration;
  assert.equal(p.ended(),true);assert.equal(p.current(),id);assert.equal(p.video.currentTime,0);assert.equal(p.video.paused,false);
  assert.equal(JSON.stringify(p.controller.exportQueue()),queue);
 }
});
test('single-track repeat leaves advertisement completion to YouTube',()=>{
 const p=page({ad:true});p.controller.command({kind:'Repeat',value:2});
 p.video.currentTime=120;p.video.paused=true;
 assert.equal(p.ended(),false);assert.equal(p.video.currentTime,120);assert.equal(p.video.paused,true);
});

test('single-track repeat recovers native auto-advance without consuming shuffle history',()=>{
 const p=page();p.controller.command({kind:'Shuffle',value:1});p.controller.command({kind:'Repeat',value:2});
 const before=JSON.stringify(p.controller.exportQueue());p.nativeAdvance();p.controller.restore({shuffle:true,repeat:2});
 assert.equal(p.controller.snapshot().switching,true);assert.equal(p.current(),ids[0]);
 assert.equal(JSON.stringify(p.controller.exportQueue()),before);
 p.controller.command({kind:'Next'});const manual=p.current();p.controller.snapshot();assert.notEqual(manual,ids[0]);
 p.advanceTime(3100);p.nativeAdvance();p.controller.snapshot();assert.equal(p.current(),manual);
});
test('repeat target survives full page replacement and manual next works without shuffle',()=>{
 const initial=page();initial.controller.command({kind:'Repeat',value:2});
 const queue=JSON.parse(JSON.stringify(initial.controller.exportQueue()));const rebuilt=page({index:1});
 rebuilt.controller.restore({shuffle:false,repeat:2,queue});rebuilt.controller.snapshot();assert.equal(rebuilt.current(),ids[0]);
 rebuilt.controller.command({kind:'Next'});rebuilt.controller.snapshot();assert.equal(rebuilt.current(),ids[1]);
 rebuilt.controller.command({kind:'Previous'});rebuilt.controller.snapshot();assert.equal(rebuilt.current(),ids[0]);
 rebuilt.controller.command({kind:'Repeat',value:0});rebuilt.nativeAdvance();rebuilt.controller.snapshot();assert.equal(rebuilt.current(),ids[1]);
 assert.equal(rebuilt.controller.exportQueue(),null);
});

test('repeat recovery does not reload a track during an advertisement',()=>{
 const p=page({ad:true});p.controller.command({kind:'Repeat',value:2});p.nativeAdvance();p.controller.snapshot();
 assert.equal(p.current(),ids[1]);assert.equal(p.jumps.length,0);
});
