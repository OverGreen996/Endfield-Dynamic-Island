if (!window.islandMusic) {
  const controller = { repeat:0, shuffle:null, baseline:null, restoredPlayer:null, restoredVideo:'', queue:null };
  const player = () => document.getElementById('movie_player');
  const video = () => document.querySelector('video');
  const playlist = () => [...new Set((player()?.getPlaylist?.()||[]).filter(id=>/^[A-Za-z0-9_-]{11}$/.test(id)))].slice(0,10000);
  const currentId = () => player()?.getVideoData?.().video_id||'';
  const syncQueue = () => {
    const id=currentId(),ids=playlist();
    if(!controller.queue)controller.queue={bag:ids.filter(x=>x!==id),history:id?[id]:[],cursor:id?0:-1,pending:'',pendingUntil:0};
    const q=controller.queue;
    // YouTube temporarily clears playlist metadata during SPA/ad transitions.
    // A provisional empty list is not evidence that unplayed tracks disappeared.
    if(ids.length<2||!ids.includes(id))return q;
    q.bag=q.bag.filter(x=>ids.includes(x)&&x!==id);
    if(q.pending&&q.pending!==id&&Date.now()<q.pendingUntil)return q;
    q.pending='';
    if(id&&q.history[q.cursor]!==id){q.history=q.history.slice(0,q.cursor+1);q.history.push(id);if(q.history.length>256)q.history.shift();q.cursor=q.history.length-1;}
    return q;
  };
  const jump = (id) => {
    const p=player(),list=p?.getPlaylist?.()||[],index=list.indexOf(id);
    if(index<0||typeof p.playVideoAt!=='function')return false;
    controller.queue.pending=id;controller.queue.pendingUntil=Date.now()+5000;
    p.playVideoAt(index);p.playVideo?.();return true;
  };
  controller.exportQueue = () => controller.shuffle ? syncQueue() : null;
  controller.nextShuffled = (automatic=false) => {
    const q=syncQueue(),id=currentId(),ids=playlist();
    if(ids.length<2||q.pending)return false;
    if(q.cursor<q.history.length-1){q.cursor++;return jump(q.history[q.cursor]);}
    if(!q.bag.length){if(automatic&&controller.repeat!==1)return false;q.bag=ids.filter(x=>x!==id);}
    const index=Math.floor(Math.random()*q.bag.length),target=q.bag.splice(index,1)[0];
    if(!target)return false;
    q.history.push(target);if(q.history.length>256)q.history.shift();q.cursor=q.history.length-1;
    return jump(target);
  };
  document.addEventListener('ended',event=>{
    if(event.target?.tagName!=='VIDEO'||controller.repeat===2||document.querySelector('#movie_player.ad-showing'))return;
    if(controller.shuffle){
      event.stopImmediatePropagation();
      if(!controller.nextShuffled(true))player()?.pauseVideo?.();
      return;
    }
    if(controller.repeat!==1)return;
    const p=player(),list=p?.getPlaylist?.()||[];
    if(list.length&&p.getPlaylistIndex?.()===list.length-1&&typeof p.playVideoAt==='function'){
      event.stopImmediatePropagation();p.playVideoAt(0);p.playVideo();
    }
  },true);
  controller.snapshot = () => {
    if(location.pathname==='/playlist'){
      const listId=new URL(location.href).searchParams.get('list');
      const link=Array.from(document.querySelectorAll('a[href]')).find(a=>{try{const u=new URL(a.href,location.origin);return u.hostname==='www.youtube.com'&&u.pathname==='/watch'&&u.searchParams.get('list')===listId&&/^[A-Za-z0-9_-]{11}$/.test(u.searchParams.get('v')||'');}catch{return false;}});
      const id=link?new URL(link.href,location.origin).searchParams.get('v'):null;
      return {firstVideo:/^[A-Za-z0-9_-]{11}$/.test(id||'')?id:''};
    }
    const p=player(),v=video(),d=p?.getVideoData?.()||{},m=navigator.mediaSession?.metadata;
    if(p&&v&&!controller.started){controller.started=true;p.playVideo?.();v.play().catch(()=>{});}
    const list=p?.getPlaylist?.()||[];
    if(!controller.baseline&&list.length)controller.baseline=[...list];
    const shuffleButton=document.querySelector('ytd-playlist-panel-renderer button[aria-pressed][aria-label*="隨機"],ytd-playlist-panel-renderer button[aria-pressed][aria-label*="Shuffle"],ytd-playlist-panel-renderer button[aria-pressed][aria-label*="shuffle"]');
    const changed=controller.baseline&&list.some((id,i)=>id!==controller.baseline[i]);
    const ad=!!document.querySelector('#movie_player.ad-showing');
    const ready=!!p&&!!v&&v.readyState>=2;
    if(v && controller.repeat===2 && !ad){v.loop=true;p.setLoopVideo?.(true);}
    const duration=Number.isFinite(v?.duration)?v.duration:0;
    const queue=controller.exportQueue();
    const switching=!!queue?.pending&&queue.pending!==d.video_id&&Date.now()<queue.pendingUntil;
    return {ready,title:String(m?.title||d.title||'YouTube 播放清單').slice(0,512),artist:String(m?.artist||d.author||'YouTube').slice(0,512),video:/^[A-Za-z0-9_-]{11}$/.test(d.video_id||'')?d.video_id:'',playing:!!v&&!v.paused,position:Number.isFinite(v?.currentTime)?v.currentTime:0,duration,shuffle:controller.shuffle??(!!changed||shuffleButton?.getAttribute('aria-pressed')==='true'),repeat:controller.repeat,count:Math.min(list.length,10000),error:String(document.querySelector('.ytp-error-content-wrap')?.innerText||'').slice(0,200),ad,canShuffle:typeof p?.playVideoAt==='function'&&list.length>1,canRepeat:typeof p?.setLoop==='function'&&typeof p?.setLoopVideo==='function',queue,switching};
  };
  controller.command = ({kind,value,force=false}) => {
    const p=player(),v=video();if(!p||!v)return false;
    if(kind==='Play'){p.playVideo();return true;}
    if(kind==='Pause'){HTMLMediaElement.prototype.pause.call(v);return v.paused;}
    if(v.readyState<2)return false;
    if(kind==='Previous'){
      if(controller.shuffle){const q=syncQueue();if(q.pending||q.cursor<1)return false;q.cursor--;return jump(q.history[q.cursor]);}
      p.previousVideo();return true;
    }
    if(kind==='Next'){if(controller.shuffle)return controller.nextShuffled();p.nextVideo();return true;}
    if(kind==='Seek'){if(!Number.isFinite(v.duration))return false;p.seekTo(Math.max(0,Math.min(v.duration,value)),true);return true;}
    if(kind==='Shuffle'&&(value===0||value===1)){
      if(playlist().length<2||typeof p.playVideoAt!=='function')return false;
      if(value===1&&typeof p.getShuffle==='function'&&p.getShuffle())p.setShuffle?.(false);
      controller.shuffle=value===1;
      if(controller.shuffle)syncQueue();else controller.queue=null;
      return true;
    }
    if(kind==='Repeat'&&[0,1,2].includes(value)&&typeof p.setLoop==='function'&&typeof p.setLoopVideo==='function'){
      p.setLoop(value===1);p.setLoopVideo(value===2);v.loop=value===2;
      if(!!p.getLoopVideo?.()!==(value===2))return false;
      controller.repeat=value;return true;
    }
    return false;
  };
  controller.restore = ({shuffle=null,repeat=null,queue=null}) => {
    const p=player(),v=video(),id=p?.getVideoData?.().video_id||'';
    if(!p||!v||v.readyState<2||!id||document.querySelector('#movie_player.ad-showing'))return;
    if(!controller.queue&&queue&&Array.isArray(queue.bag)&&queue.bag.length<=10000&&Array.isArray(queue.history)&&queue.history.length<=256&&
      queue.bag.concat(queue.history).every(x=>typeof x==='string'&&/^[A-Za-z0-9_-]{11}$/.test(x))&&
      Number.isInteger(queue.cursor)&&queue.cursor>=-1&&queue.cursor<queue.history.length&&typeof queue.pending==='string'&&Number.isFinite(queue.pendingUntil))
      controller.queue=JSON.parse(JSON.stringify(queue));
    if(controller.restoredPlayer===p&&controller.restoredVideo===id)return;
    // The host owns preferences across complete navigations. Reapply once per
    // ready track/player rather than treating a rebuilt DOM as "shuffle off".
    let applied=true;
    if(typeof shuffle==='boolean')applied=controller.command({kind:'Shuffle',value:shuffle?1:0,force:true})&&applied;
    if([0,1,2].includes(repeat))applied=controller.command({kind:'Repeat',value:repeat})&&applied;
    if(applied){controller.restoredPlayer=p;controller.restoredVideo=id;}
  };
  window.islandMusic=controller;
}
