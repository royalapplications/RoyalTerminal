// One FIFO per controller lease, not per page. Plain typing has no promise/queue
// overhead unless an intercepted key is waiting for a callback or VT encoding.
export class PaneInputLane {
  constructor(isCurrent, transmit, report) { this.isCurrent=isCurrent;this.transmit=transmit;this.report=report;this.count=0;this.bytes=0;this.tail=Promise.resolve(); }
  write(frame){
    if(!this.isCurrent())return;
    if(this.count===0){this.transmit(frame);return;}
    void this.enqueue(()=>frame,frame.data?.length??0).catch(this.report);
  }
  enqueue(resolve,charge=0){
    if(this.count>=128||charge>256*1024-this.bytes)return Promise.reject(Error('Pending keyboard input limit reached. This input was not sent.'));
    this.count++;this.bytes+=charge;
    const result=this.tail.then(async()=>{
      if(!this.isCurrent())return;
      const frame=await resolve();
      if(frame&&this.isCurrent())this.transmit(frame);
    }).finally(()=>{this.count--;this.bytes-=charge;});
    // Errors belong to their own item; never retry or block subsequent owned input.
    this.tail=result.catch(()=>{});return result;
  }
}
